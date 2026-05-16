package com.vkenterprises.vras.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.vras.data.api.ApiService
import com.vkenterprises.vras.data.models.AdminAddSubRequest
import com.vkenterprises.vras.data.models.AdminUserItem
import com.vkenterprises.vras.data.models.SetUserFlagRequest
import com.vkenterprises.vras.data.models.SubscriptionRecord
import com.vkenterprises.vras.data.models.VerifyAdminPassRequest
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class ControlPanelUiState(
    val passwordGate: Boolean          = true,
    val passwordInput: String          = "",
    val passwordError: String?         = null,
    val verifying: Boolean             = false,
    val users: List<AdminUserItem>     = emptyList(),
    val usersLoading: Boolean          = false,
    val filterText: String             = "",
    val selectedUser: AdminUserItem?   = null,
    val subs: List<SubscriptionRecord> = emptyList(),
    val subsLoading: Boolean           = false,
    val showAddDialog: Boolean         = false,
    val addStartDate: String           = "",
    val addEndDate: String             = "",
    val addAmount: String              = "",
    val addNotes: String               = "",
    val addError: String?              = null,
    val adding: Boolean                = false,
    val busy: Boolean                  = false,
    val errorMsg: String?              = null
)

@HiltViewModel
class ControlPanelViewModel @Inject constructor(
    private val api: ApiService
) : ViewModel() {

    private val _ui = MutableStateFlow(ControlPanelUiState())
    val ui: StateFlow<ControlPanelUiState> = _ui.asStateFlow()

    private var adminUserId: Long = -1L

    fun init(userId: Long) { adminUserId = userId }

    fun onPasswordChange(v: String) = _ui.update { it.copy(passwordInput = v, passwordError = null) }

    fun verifyPassword() {
        val pass = _ui.value.passwordInput.trim()
        if (pass.isBlank()) {
            _ui.update { it.copy(passwordError = "Enter your Control Panel password") }
            return
        }
        viewModelScope.launch {
            _ui.update { it.copy(verifying = true, passwordError = null) }
            runCatching {
                val resp = api.verifyAdminPass(adminUserId, VerifyAdminPassRequest(pass))
                if (resp.isSuccessful) {
                    _ui.update { it.copy(verifying = false, passwordGate = false) }
                    loadUsers()
                } else {
                    _ui.update { it.copy(verifying = false,
                        passwordError = "Incorrect password — or no password set by your admin") }
                }
            }.onFailure {
                _ui.update { it.copy(verifying = false, passwordError = "Network error. Try again.") }
            }
        }
    }

    private fun loadUsers() {
        viewModelScope.launch {
            _ui.update { it.copy(usersLoading = true, errorMsg = null) }
            runCatching {
                val resp = api.getAdminUsers(adminUserId)
                if (resp.isSuccessful)
                    _ui.update { it.copy(usersLoading = false, users = resp.body() ?: emptyList()) }
                else
                    _ui.update { it.copy(usersLoading = false, errorMsg = "Failed to load users") }
            }.onFailure {
                _ui.update { it.copy(usersLoading = false, errorMsg = "Network error. Try again.") }
            }
        }
    }

    fun onFilterChange(v: String) = _ui.update { it.copy(filterText = v) }

    fun selectUser(user: AdminUserItem) {
        _ui.update { it.copy(selectedUser = user, subs = emptyList(), subsLoading = true) }
        viewModelScope.launch {
            runCatching {
                val resp = api.getUserSubscriptions(adminUserId, user.id)
                if (resp.isSuccessful)
                    _ui.update { it.copy(subsLoading = false, subs = resp.body() ?: emptyList()) }
                else
                    _ui.update { it.copy(subsLoading = false, errorMsg = "Failed to load plans") }
            }.onFailure {
                _ui.update { it.copy(subsLoading = false, errorMsg = "Network error. Try again.") }
            }
        }
    }

    fun clearUser() = _ui.update { it.copy(selectedUser = null, subs = emptyList()) }

    // ── User state toggles ─────────────────────────────────────────────────
    private fun mutateSelected(transform: (AdminUserItem) -> AdminUserItem) {
        val cur = _ui.value.selectedUser ?: return
        val updated = transform(cur)
        _ui.update { st ->
            st.copy(
                selectedUser = updated,
                users = st.users.map { if (it.id == updated.id) updated else it }
            )
        }
    }

    fun setActive(active: Boolean) {
        val user = _ui.value.selectedUser ?: return
        viewModelScope.launch {
            _ui.update { it.copy(busy = true) }
            runCatching {
                val resp = api.adminSetActive(adminUserId, user.id, SetUserFlagRequest(active))
                if (resp.isSuccessful) mutateSelected { it.copy(isActive = active) }
                else _ui.update { it.copy(errorMsg = "Failed to update active state") }
            }.onFailure { _ui.update { it.copy(errorMsg = "Network error. Try again.") } }
            _ui.update { it.copy(busy = false) }
        }
    }

    fun setStopped(stopped: Boolean) {
        val user = _ui.value.selectedUser ?: return
        viewModelScope.launch {
            _ui.update { it.copy(busy = true) }
            runCatching {
                val resp = api.adminSetStopped(adminUserId, user.id, SetUserFlagRequest(stopped))
                if (resp.isSuccessful) mutateSelected { it.copy(isStopped = stopped) }
                else _ui.update { it.copy(errorMsg = "Failed to update stop state") }
            }.onFailure { _ui.update { it.copy(errorMsg = "Network error. Try again.") } }
            _ui.update { it.copy(busy = false) }
        }
    }

    fun setBlacklisted(blacklisted: Boolean) {
        val user = _ui.value.selectedUser ?: return
        viewModelScope.launch {
            _ui.update { it.copy(busy = true) }
            runCatching {
                val resp = api.adminSetBlacklisted(adminUserId, user.id, SetUserFlagRequest(blacklisted))
                if (resp.isSuccessful)
                    mutateSelected { it.copy(isBlacklisted = blacklisted, isStopped = blacklisted) }
                else _ui.update { it.copy(errorMsg = "Failed to update blacklist state") }
            }.onFailure { _ui.update { it.copy(errorMsg = "Network error. Try again.") } }
            _ui.update { it.copy(busy = false) }
        }
    }

    // ── Subscriptions ──────────────────────────────────────────────────────
    fun showAddDialog() = _ui.update {
        it.copy(showAddDialog = true, addStartDate = "", addEndDate = "",
            addAmount = "", addNotes = "", addError = null)
    }
    fun hideAddDialog() = _ui.update { it.copy(showAddDialog = false, addError = null) }
    fun onAddStartDate(v: String) = _ui.update { it.copy(addStartDate = v) }
    fun onAddEndDate(v: String)   = _ui.update { it.copy(addEndDate = v) }
    fun onAddAmount(v: String)    = _ui.update { it.copy(addAmount = v) }
    fun onAddNotes(v: String)     = _ui.update { it.copy(addNotes = v) }

    fun addSubscription() {
        val st = _ui.value
        val userId = st.selectedUser?.id ?: return
        val amount = st.addAmount.toDoubleOrNull()
        if (st.addStartDate.isBlank() || st.addEndDate.isBlank() || amount == null) {
            _ui.update { it.copy(addError = "Start date, end date and amount are required") }
            return
        }
        if (st.subs.isNotEmpty()) {
            _ui.update { it.copy(addError = "This user already has a plan — delete it first") }
            return
        }
        viewModelScope.launch {
            _ui.update { it.copy(adding = true, addError = null) }
            runCatching {
                val resp = api.adminAddSubscription(
                    adminUserId, userId,
                    AdminAddSubRequest(st.addStartDate, st.addEndDate, amount, st.addNotes.ifBlank { null })
                )
                if (resp.isSuccessful) {
                    _ui.update { it.copy(adding = false, showAddDialog = false) }
                    st.selectedUser?.let { selectUser(it) }
                } else {
                    _ui.update { it.copy(adding = false, addError = "Failed to add plan") }
                }
            }.onFailure {
                _ui.update { it.copy(adding = false, addError = "Network error. Try again.") }
            }
        }
    }

    fun deleteSubscription(subId: Long) {
        val user = _ui.value.selectedUser ?: return
        viewModelScope.launch {
            runCatching {
                val resp = api.adminDeleteSubscription(adminUserId, subId)
                if (resp.isSuccessful) selectUser(user)
                else _ui.update { it.copy(errorMsg = "Failed to delete plan") }
            }.onFailure { _ui.update { it.copy(errorMsg = "Network error. Try again.") } }
        }
    }

    fun dismissError() = _ui.update { it.copy(errorMsg = null) }
}
