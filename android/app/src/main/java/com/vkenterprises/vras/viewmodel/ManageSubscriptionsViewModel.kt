package com.vkenterprises.vras.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.vras.data.api.ApiService
import com.vkenterprises.vras.data.models.AdminAddSubRequest
import com.vkenterprises.vras.data.models.AdminUserItem
import com.vkenterprises.vras.data.models.SubscriptionRecord
import com.vkenterprises.vras.data.models.VerifySubsPassRequest
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class ManageSubsUiState(
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
    val errorMsg: String?              = null
)

@HiltViewModel
class ManageSubscriptionsViewModel @Inject constructor(
    private val api: ApiService
) : ViewModel() {

    private val _ui = MutableStateFlow(ManageSubsUiState())
    val ui: StateFlow<ManageSubsUiState> = _ui.asStateFlow()

    private var adminUserId: Long = -1L

    fun init(userId: Long) {
        adminUserId = userId
    }

    fun onPasswordChange(v: String) = _ui.update { it.copy(passwordInput = v, passwordError = null) }

    fun verifyPassword() {
        val pass = _ui.value.passwordInput.trim()
        if (pass.isBlank()) {
            _ui.update { it.copy(passwordError = "Enter the subscription management password") }
            return
        }
        viewModelScope.launch {
            _ui.update { it.copy(verifying = true, passwordError = null) }
            runCatching {
                val resp = api.verifySubsPass(adminUserId, VerifySubsPassRequest(pass))
                if (resp.isSuccessful) {
                    _ui.update { it.copy(verifying = false, passwordGate = false) }
                    loadUsers()
                } else {
                    _ui.update { it.copy(verifying = false, passwordError = "Incorrect password") }
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
                if (resp.isSuccessful) {
                    _ui.update { it.copy(usersLoading = false, users = resp.body() ?: emptyList()) }
                } else {
                    _ui.update { it.copy(usersLoading = false, errorMsg = "Failed to load users") }
                }
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
                if (resp.isSuccessful) {
                    _ui.update { it.copy(subsLoading = false, subs = resp.body() ?: emptyList()) }
                } else {
                    _ui.update { it.copy(subsLoading = false, errorMsg = "Failed to load subscriptions") }
                }
            }.onFailure {
                _ui.update { it.copy(subsLoading = false, errorMsg = "Network error. Try again.") }
            }
        }
    }

    fun clearUser() = _ui.update { it.copy(selectedUser = null, subs = emptyList()) }

    fun showAddDialog() = _ui.update {
        it.copy(showAddDialog = true, addStartDate = "", addEndDate = "", addAmount = "", addNotes = "", addError = null)
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
        viewModelScope.launch {
            _ui.update { it.copy(adding = true, addError = null) }
            runCatching {
                val resp = api.adminAddSubscription(
                    adminUserId, userId,
                    AdminAddSubRequest(st.addStartDate, st.addEndDate, amount, st.addNotes.ifBlank { null })
                )
                if (resp.isSuccessful) {
                    _ui.update { it.copy(adding = false, showAddDialog = false) }
                    selectUser(st.selectedUser)
                } else {
                    _ui.update { it.copy(adding = false, addError = "Failed to add subscription") }
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
                else _ui.update { it.copy(errorMsg = "Failed to delete subscription") }
            }.onFailure {
                _ui.update { it.copy(errorMsg = "Network error. Try again.") }
            }
        }
    }

    fun dismissError() = _ui.update { it.copy(errorMsg = null) }
}
