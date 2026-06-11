package com.vkenterprises.crmrs.viewmodel

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.work.WorkManager
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.api.SessionTokens
import com.vkenterprises.crmrs.data.local.TenantDb
import com.vkenterprises.crmrs.data.models.AgencyListItem
import com.vkenterprises.crmrs.data.models.LoginRequest
import com.vkenterprises.crmrs.data.models.RegisterRequest
import com.vkenterprises.crmrs.data.repository.AuthRepository
import com.vkenterprises.crmrs.data.repository.AuthResult
import com.vkenterprises.crmrs.utils.DeviceIdUtil
import com.vkenterprises.crmrs.utils.PreferencesManager
import kotlinx.coroutines.flow.first
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import com.vkenterprises.crmrs.data.models.HeartbeatRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import java.time.LocalDate
import javax.inject.Inject
import kotlin.coroutines.resume

sealed class AuthUiState {
    object Idle : AuthUiState()
    object Loading : AuthUiState()
    object RegisterSuccess : AuthUiState()
    object LoginSuccess : AuthUiState()
    object SubscriptionExpired : AuthUiState()
    data class PendingApproval(val reason: String) : AuthUiState()
    data class DeviceMismatch(val reason: String) : AuthUiState()
    data class AppStopped(val message: String) : AuthUiState()
    data class Blacklisted(val message: String) : AuthUiState()
    data class Inactive(val message: String) : AuthUiState()
    data class KycPending(val message: String) : AuthUiState()
    data class KycRejected(val message: String) : AuthUiState()
    data class Error(val message: String) : AuthUiState()
}

@HiltViewModel
class AuthViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val prefs: PreferencesManager,
    private val db: TenantDb
) : ViewModel() {

    private suspend fun clearOfflineCache() {
        runCatching {
            db.vehicleCacheDao().deleteAll()
            db.branchSyncStateDao().clearAll()
        }
    }

    private val repo = AuthRepository()

    private val _state = MutableStateFlow<AuthUiState>(AuthUiState.Idle)
    val state: StateFlow<AuthUiState> = _state.asStateFlow()

    val isLoggedIn      = prefs.isLoggedIn
    val userId          = prefs.userId
    val userName        = prefs.userName
    val userMobile      = prefs.userMobile
    val isAdmin         = prefs.isAdmin
    val pfpUrl          = prefs.pfpUrl
    val subscriptionEnd = prefs.subscriptionEnd
    val blockedReason   = prefs.blockedReason
    val agencySlug      = prefs.agencySlug
    val agencyName      = prefs.agencyName
    val agencyLogo      = prefs.agencyLogo

    private val _agencies = MutableStateFlow<List<AgencyListItem>>(emptyList())
    val agencies: StateFlow<List<AgencyListItem>> = _agencies.asStateFlow()

    fun checkMobile(mobile: String, slug: String, onResult: (Boolean?, String?) -> Unit) = viewModelScope.launch {
        when (val r = repo.checkMobileRegistered(mobile.trim(), slug.trim())) {
            is AuthResult.Success -> onResult(r.data, null)
            is AuthResult.Error   -> onResult(null, r.message)
        }
    }

    fun sendOtp(mobile: String, onResult: (Boolean, String) -> Unit) = viewModelScope.launch {
        when (val r = repo.sendOtp(mobile.trim())) {
            is AuthResult.Success -> onResult(true, r.data)
            is AuthResult.Error   -> onResult(false, r.message)
        }
    }

    fun verifyOtp(mobile: String, otp: String, onResult: (Boolean, String) -> Unit) = viewModelScope.launch {
        when (val r = repo.verifyOtp(mobile.trim(), otp.trim())) {
            is AuthResult.Success -> onResult(true, r.data)
            is AuthResult.Error   -> onResult(false, r.message)
        }
    }

    fun loadAgencies() = viewModelScope.launch {
        when (val r = repo.getAgencies()) {
            is AuthResult.Success -> _agencies.value = r.data
            is AuthResult.Error   -> { }
        }
    }

    private val _kickReason = MutableStateFlow<String?>(null)
    val kickReason: StateFlow<String?> = _kickReason.asStateFlow()

    private var pollingJob: Job? = null

    @Volatile private var lastLat: Double? = null
    @Volatile private var lastLng: Double? = null

    fun clearBlockedReason() = viewModelScope.launch { prefs.clearBlockedReason() }

    fun startStatusPolling(userId: Long) {
        if (pollingJob?.isActive == true) return
        pollingJob = viewModelScope.launch(Dispatchers.IO) {
            while (true) {
                delay(15_000)
                refreshCachedLocation()
                runCatching {
                    val resp = ApiClient.api.heartbeat(HeartbeatRequest(userId, lastLat, lastLng))
                    if (resp.isSuccessful) {
                        val body = resp.body() ?: return@runCatching
                        when {
                            body.isBlacklisted -> _kickReason.value = "blacklisted"
                            body.isStopped     -> _kickReason.value = "app_stopped"
                            _kickReason.value == "app_stopped" ||
                            _kickReason.value == "blacklisted"  -> _kickReason.value = "running"
                        }
                    }
                }
            }
        }
    }

    @android.annotation.SuppressLint("MissingPermission")
    private suspend fun refreshCachedLocation() {
        val fine = androidx.core.content.ContextCompat.checkSelfPermission(
            context, android.Manifest.permission.ACCESS_FINE_LOCATION
        ) == android.content.pm.PackageManager.PERMISSION_GRANTED
        val coarse = androidx.core.content.ContextCompat.checkSelfPermission(
            context, android.Manifest.permission.ACCESS_COARSE_LOCATION
        ) == android.content.pm.PackageManager.PERMISSION_GRANTED
        if (!fine && !coarse) return
        runCatching {
            val client = com.google.android.gms.location.LocationServices
                .getFusedLocationProviderClient(context)
            val loc = kotlinx.coroutines.suspendCancellableCoroutine<android.location.Location?> { cont ->
                client.lastLocation
                    .addOnSuccessListener { cont.resume(it) }
                    .addOnFailureListener { cont.resume(null) }
                    .addOnCanceledListener { cont.resume(null) }
            }
            if (loc != null) { lastLat = loc.latitude; lastLng = loc.longitude }
        }
    }

    init {
        SessionTokens.agencySlug = BuildConfig.AGENCY_SLUG
        viewModelScope.launch {
            prefs.saveAgency(BuildConfig.AGENCY_SLUG, BuildConfig.AGENCY_NAME, "")
            val savedToken  = prefs.tenantToken.first()
            val savedUserId = prefs.userId.first()
            if (savedUserId > 0L && savedToken.isNullOrBlank()) {
                prefs.clearSession()
                SessionTokens.tenantToken = null
            } else {
                SessionTokens.tenantToken = savedToken
            }
        }
        viewModelScope.launch {
            prefs.subscriptionEnd.collect { subEnd ->
                if (!subEnd.isNullOrBlank()) {
                    runCatching {
                        val expired = java.time.LocalDate.parse(subEnd)
                            .isBefore(java.time.LocalDate.now())
                        if (expired) _state.value = AuthUiState.SubscriptionExpired
                    }
                }
            }
        }
    }

    fun register(
        mobile: String, name: String,
        address: String?, pincode: String?,
        pfpBase64: String?,
        aadhaarFront: String?, aadhaarBack: String?,
        panFront: String?,
        accountNumber: String?, ifscCode: String?,
        slug: String, agencyName: String, agencyMobile: String,
        agencyLogo: String = "",
        selfieWithAadhaar: String? = null,
        aadhaarNumber: String? = null, aadhaarName: String? = null,
        aadhaarDob: String? = null, aadhaarGender: String? = null,
        aadhaarAddress: String? = null, aadhaarVerified: Boolean = false,
        regLat: Double? = null, regLng: Double? = null, regLocation: String? = null,
        aadhaarPhoto: String? = null
    ) = viewModelScope.launch {
        lastMobile = mobile.trim()
        _state.value = AuthUiState.Loading
        val deviceId = DeviceIdUtil.get(context)
        val result = repo.register(
            RegisterRequest(
                mobile.trim(), name.trim(),
                address?.trim(), pincode?.trim(),
                pfpBase64, deviceId,
                aadhaarFront, aadhaarBack, panFront,
                accountNumber?.trim(), ifscCode?.trim(),
                slug.trim(), agencyMobile.trim(),
                selfieWithAadhaar,
                aadhaarNumber, aadhaarName, aadhaarDob, aadhaarGender,
                aadhaarAddress, aadhaarVerified,
                regLat, regLng, regLocation,
                aadhaarPhoto
            )
        )
        _state.value = when (result) {
            is AuthResult.Success -> {
                prefs.saveAgency(slug.trim(), agencyName, agencyLogo)
                AuthUiState.RegisterSuccess
            }
            is AuthResult.Error   -> AuthUiState.Error(result.message)
        }
    }

    var lastMobile: String = ""
        private set

    var lastKycMessage: String = ""
        private set

    fun login(mobile: String, slug: String, agencyName: String, agencyLogo: String = "") = viewModelScope.launch {
        lastMobile = mobile.trim()
        _state.value = AuthUiState.Loading
        val newSlug  = slug.trim()
        val prevSlug = prefs.agencySlug.first()
        if (prevSlug != null && prevSlug != newSlug)
            clearOfflineCache()

        SessionTokens.agencySlug = newSlug

        val deviceId = DeviceIdUtil.get(context)
        val result = repo.login(LoginRequest(mobile.trim(), deviceId, slug.trim()))
        _state.value = when (result) {
            is AuthResult.Success -> {
                val user = result.data
                SessionTokens.tenantToken = user.tenantToken
                prefs.saveAgency(slug.trim(), agencyName, agencyLogo)
                prefs.saveSession(
                    user.userId ?: 0L, user.name ?: "", user.mobile ?: "",
                    user.isAdmin, user.subscriptionEndDate, user.pfpUrl,
                    user.tenantToken
                )
                val subEnd = user.subscriptionEndDate
                if (subEnd != null && LocalDate.parse(subEnd).isBefore(LocalDate.now()))
                    AuthUiState.SubscriptionExpired
                else
                    AuthUiState.LoginSuccess
            }
            is AuthResult.Error -> when (result.reason) {
                "kyc_pending"      -> AuthUiState.KycPending(result.message).also { lastKycMessage = result.message }
                "kyc_failed"       -> AuthUiState.KycRejected(result.message).also { lastKycMessage = result.message }
                "pending_approval" -> AuthUiState.PendingApproval(result.message)
                "device_mismatch"  -> AuthUiState.DeviceMismatch(result.message)
                "app_stopped"      -> AuthUiState.AppStopped(result.message)
                "blacklisted"      -> AuthUiState.Blacklisted(result.message)
                "inactive"         -> AuthUiState.Inactive(result.message)
                else               -> AuthUiState.Error(result.message)
            }
        }
    }

    fun refreshSession() = viewModelScope.launch {
        val uid = prefs.userId.first()
        if (uid <= 0L) return@launch
        runCatching {
            val resp = ApiClient.api.getProfile(uid)
            if (resp.isSuccessful) {
                val p = resp.body() ?: return@launch
                prefs.saveSession(
                    userId  = uid,
                    name    = p.name,
                    mobile  = p.mobile,
                    isAdmin = p.isAdmin,
                    subEnd  = p.subscriptions.filter { it.isActive }
                                  .maxOfOrNull { it.endDate },
                    pfp     = p.pfpUrl
                )
            }
        }
    }

    fun logout() = viewModelScope.launch {
        pollingJob?.cancel()
        pollingJob = null
        _kickReason.value = null
        SessionTokens.tenantToken = null
        runCatching { WorkManager.getInstance(context).cancelAllWork() }
        clearOfflineCache()
        SessionTokens.agencySlug = null
        prefs.clearSession()
        _state.value = AuthUiState.Idle
    }

    fun resetState() { _state.value = AuthUiState.Idle }
}
