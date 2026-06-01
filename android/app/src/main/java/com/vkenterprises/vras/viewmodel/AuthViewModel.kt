package com.vkenterprises.vras.viewmodel

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.work.WorkManager
import com.vkenterprises.vras.BuildConfig
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.api.SessionTokens
import com.vkenterprises.vras.data.local.TenantDb
import com.vkenterprises.vras.data.models.AgencyListItem
import com.vkenterprises.vras.data.models.LoginRequest
import com.vkenterprises.vras.data.models.RegisterRequest
import com.vkenterprises.vras.data.repository.AuthRepository
import com.vkenterprises.vras.data.repository.AuthResult
import com.vkenterprises.vras.utils.DeviceIdUtil
import com.vkenterprises.vras.utils.PreferencesManager
import kotlinx.coroutines.flow.first
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import com.vkenterprises.vras.data.models.HeartbeatRequest
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

    // Wipes every locally-cached record from the CURRENT agency's DB file.
    // TenantDb gives us per-agency isolation (each agency has its own
    // vk_cache_<slug>.db); this additionally deletes the rows on agency
    // switch / logout, so nothing of the previous agency lingers on disk.
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
    val blockedReason   = prefs.blockedReason  // used by background heartbeat worker
    val agencySlug      = prefs.agencySlug
    val agencyName      = prefs.agencyName
    val agencyLogo      = prefs.agencyLogo

    // Approved agencies for the register / login picker.
    private val _agencies = MutableStateFlow<List<AgencyListItem>>(emptyList())
    val agencies: StateFlow<List<AgencyListItem>> = _agencies.asStateFlow()

    fun loadAgencies() = viewModelScope.launch {
        when (val r = repo.getAgencies()) {
            is AuthResult.Success -> _agencies.value = r.data
            is AuthResult.Error   -> { /* leave empty — the screen shows a hint */ }
        }
    }

    // Direct in-memory signal for foreground status polling — no DataStore delay
    private val _kickReason = MutableStateFlow<String?>(null)
    val kickReason: StateFlow<String?> = _kickReason.asStateFlow()

    private var pollingJob: Job? = null

    // Cached last-known device location, refreshed every ~10s by the polling
    // loop and attached to every foreground heartbeat. Previously the
    // foreground heartbeat sent null lat/lng, so an online agent's location
    // only ever updated via the 15-min background LocationWorker — which Doze
    // / battery optimisation frequently deferred, leaving agents who were
    // clearly online with no pin on the admin's live map. Sending the cached
    // fix here keeps every open-app agent's location fresh within seconds.
    @Volatile private var lastLat: Double? = null
    @Volatile private var lastLng: Double? = null

    fun clearBlockedReason() = viewModelScope.launch { prefs.clearBlockedReason() }

    fun startStatusPolling(userId: Long) {
        if (pollingJob?.isActive == true) return
        pollingJob = viewModelScope.launch(Dispatchers.IO) {
            var tick = 0
            while (true) {
                delay(2_000)
                // Refresh the cached fix every 5th tick (~10s). lastLocation is
                // the OS-cached position — cheap, no GPS spin-up — so this is
                // negligible cost while keeping the pin current.
                if (tick % 5 == 0) refreshCachedLocation()
                tick++
                runCatching {
                    val resp = ApiClient.api.heartbeat(HeartbeatRequest(userId, lastLat, lastLng))
                    if (resp.isSuccessful) {
                        val body = resp.body() ?: return@runCatching
                        when {
                            body.isBlacklisted -> _kickReason.value = "blacklisted"
                            body.isStopped     -> _kickReason.value = "app_stopped"
                            // Was blocked but admin has re-enabled — signal recovery
                            _kickReason.value == "app_stopped" ||
                            _kickReason.value == "blacklisted"  -> _kickReason.value = "running"
                        }
                    }
                }
            }
        }
    }

    // Pulls the OS-cached location (any app's last fix) into lastLat/lastLng.
    // No-ops silently if neither fine nor coarse permission is granted.
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
        // White-label build: the agency slug is baked in at build time. Every
        // request — including TenantDb's per-agency Room file — uses this one
        // slug; there is no in-app agency picker any more.
        SessionTokens.agencySlug = BuildConfig.AGENCY_SLUG
        viewModelScope.launch {
            // Persist the baked-in slug + name so the rest of the codebase
            // (which reads from prefs) keeps working without changes.
            prefs.saveAgency(BuildConfig.AGENCY_SLUG, BuildConfig.AGENCY_NAME, "")
            val savedToken  = prefs.tenantToken.first()
            val savedUserId = prefs.userId.first()
            // Stale session (logged in before tenant tokens existed): clear it so
            // the user is forced to re-login and the server issues a fresh token.
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
        // Registration-time KYC
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

    // Last mobile a user tried to log in with — used by the KYC re-submit flow
    // (a rejected agent has no session/token, so resubmit is keyed by mobile).
    var lastMobile: String = ""
        private set

    // Message from the most recent KYC pending/rejected login — shown on the
    // KYC status screens (the login state is reset right after navigation).
    var lastKycMessage: String = ""
        private set

    fun login(mobile: String, slug: String, agencyName: String, agencyLogo: String = "") = viewModelScope.launch {
        lastMobile = mobile.trim()
        _state.value = AuthUiState.Loading
        val newSlug  = slug.trim()
        val prevSlug = prefs.agencySlug.first()
        // If the user is switching to a different agency, wipe the PREVIOUS
        // agency's offline DB rows BEFORE TenantDb swaps to the new file. Even
        // though the per-agency files are already isolated by name, this
        // guarantees nothing of the previous agency stays on disk either.
        if (prevSlug != null && prevSlug != newSlug)
            clearOfflineCache()

        // Point TenantDb at the new agency's file. Every later DAO call —
        // including the very first one HomeScreen makes after navigation —
        // opens vk_cache_<newSlug>.db, which is fresh / empty.
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
                // Refreshing the saved session every screen-resume keeps the
                // pfp URL in lockstep with the server. Without this, if a
                // user logged in before the AbsUrl fix shipped, prefs would
                // hold a stale http://localhost:5001/... URL and the home
                // top-bar avatar would render broken until a fresh login.
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
        // Cancel any scheduled sync workers FIRST so they can't repopulate the
        // cache as we're wiping it. Then wipe the current agency's offline
        // rows. Finally drop the slug — any later DAO call resolves to the
        // empty "none" DB, not to a previously-signed-in agency's file.
        SessionTokens.tenantToken = null
        runCatching { WorkManager.getInstance(context).cancelAllWork() }
        clearOfflineCache()
        SessionTokens.agencySlug = null
        prefs.clearSession()
        _state.value = AuthUiState.Idle
    }

    fun resetState() { _state.value = AuthUiState.Idle }
}
