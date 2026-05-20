package com.vkenterprises.vras.viewmodel

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.work.WorkManager
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

    fun clearBlockedReason() = viewModelScope.launch { prefs.clearBlockedReason() }

    fun startStatusPolling(userId: Long) {
        if (pollingJob?.isActive == true) return
        pollingJob = viewModelScope.launch(Dispatchers.IO) {
            while (true) {
                delay(2_000)
                runCatching {
                    val resp = ApiClient.api.heartbeat(HeartbeatRequest(userId, null, null))
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

    init {
        // Restore the tenant token AND the active agency slug into memory so
        // authed requests route correctly, and so TenantDb opens the right
        // per-agency database, even before the first interaction this session.
        viewModelScope.launch {
            SessionTokens.tenantToken = prefs.tenantToken.first()
            SessionTokens.agencySlug  = prefs.agencySlug.first()
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
        slug: String, agencyName: String, agencyMobile: String
    ) = viewModelScope.launch {
        _state.value = AuthUiState.Loading
        val deviceId = DeviceIdUtil.get(context)
        val result = repo.register(
            RegisterRequest(
                mobile.trim(), name.trim(),
                address?.trim(), pincode?.trim(),
                pfpBase64, deviceId,
                aadhaarFront, aadhaarBack, panFront,
                accountNumber?.trim(), ifscCode?.trim(),
                slug.trim(), agencyMobile.trim()
            )
        )
        _state.value = when (result) {
            is AuthResult.Success -> {
                prefs.saveAgency(slug.trim(), agencyName)
                AuthUiState.RegisterSuccess
            }
            is AuthResult.Error   -> AuthUiState.Error(result.message)
        }
    }

    fun login(mobile: String, slug: String, agencyName: String) = viewModelScope.launch {
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
                prefs.saveAgency(slug.trim(), agencyName)
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
                                  .maxOfOrNull { it.endDate }
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
