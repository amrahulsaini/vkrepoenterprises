package com.vkenterprises.crmrs.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.crmrs.data.api.ApiService
import com.vkenterprises.crmrs.data.models.RepoTaskEditRequest
import com.vkenterprises.crmrs.data.models.RepoTaskItem
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.Month
import java.time.format.TextStyle
import java.util.Locale
import javax.inject.Inject

data class TaskManagerUiState(
    val loading: Boolean            = false,
    val items: List<RepoTaskItem>   = emptyList(),
    val demand: Int                 = 0,
    val target: Int                 = 0,
    val billedThisMonth: Int        = 0,
    val year: Int                   = LocalDate.now().year,
    val month: Int                  = LocalDate.now().monthValue,
    val editing: RepoTaskItem?      = null,
    val saving: Boolean             = false,
    val errorMsg: String?           = null,
    val savedMsg: String?           = null
) {
    val monthName: String
        get() = Month.of(month).getDisplayName(TextStyle.FULL, Locale.ENGLISH).uppercase()

    val demandMet: Boolean get() = demand > 0 && billedThisMonth >= demand
    val targetMet: Boolean get() = target > 0 && billedThisMonth >= target

    val progressFraction: Float
        get() {
            val goal = if (target > 0) target else demand
            if (goal <= 0) return 0f
            return (billedThisMonth.toFloat() / goal.toFloat()).coerceIn(0f, 1f)
        }
}

@HiltViewModel
class TaskManagerViewModel @Inject constructor(
    private val api: ApiService
) : ViewModel() {

    private val _ui = MutableStateFlow(TaskManagerUiState())
    val ui: StateFlow<TaskManagerUiState> = _ui.asStateFlow()

    private var userId: Long = -1L

    fun init(uid: Long) {
        if (uid <= 0 || userId == uid) return
        userId = uid
        load()
    }

    fun load() {
        if (userId <= 0) return
        val st = _ui.value
        viewModelScope.launch {
            _ui.update { it.copy(loading = true, errorMsg = null) }
            runCatching {
                val resp = api.getTasks(userId, st.year, st.month)
                if (resp.isSuccessful) {
                    val b = resp.body()
                    if (b != null) {
                        _ui.update {
                            it.copy(
                                loading = false,
                                items = b.items,
                                demand = b.demand,
                                target = b.target,
                                billedThisMonth = b.billedThisMonth,
                                year = b.year,
                                month = b.month
                            )
                        }
                    } else _ui.update { it.copy(loading = false, errorMsg = "No data") }
                } else {
                    _ui.update { it.copy(loading = false, errorMsg = "Failed to load tasks") }
                }
            }.onFailure {
                _ui.update { it.copy(loading = false, errorMsg = "Network error. Try again.") }
            }
        }
    }

    fun prevMonth() {
        val st = _ui.value
        val d = LocalDate.of(st.year, st.month, 1).minusMonths(1)
        _ui.update { it.copy(year = d.year, month = d.monthValue) }
        load()
    }

    fun nextMonth() {
        val st = _ui.value
        val d = LocalDate.of(st.year, st.month, 1).plusMonths(1)
        val now = LocalDate.now()
        if (d.isAfter(LocalDate.of(now.year, now.monthValue, 1))) return
        _ui.update { it.copy(year = d.year, month = d.monthValue) }
        load()
    }

    fun startEdit(item: RepoTaskItem) = _ui.update { it.copy(editing = item, savedMsg = null) }
    fun cancelEdit() = _ui.update { it.copy(editing = null) }
    fun dismissMessages() = _ui.update { it.copy(errorMsg = null, savedMsg = null) }

    fun saveEdit(edited: RepoTaskItem) {
        if (userId <= 0) return
        viewModelScope.launch {
            _ui.update { it.copy(saving = true, errorMsg = null) }
            runCatching {
                val resp = api.updateTask(
                    edited.id, userId,
                    RepoTaskEditRequest(
                        loanNo               = edited.loanNo,
                        customerName         = edited.customerName,
                        vehicleNo            = edited.vehicleNo,
                        model                = edited.model,
                        chassisNo            = edited.chassisNo,
                        engineNo             = edited.engineNo,
                        branchName           = edited.branchName,
                        agentName            = edited.agentName,
                        parkingYardName      = edited.parkingYardName,
                        parkingYardMobile    = edited.parkingYardMobile,
                        loadDetails          = edited.loadDetails,
                        addlChargesNotes     = edited.addlChargesNotes,
                        addlChargesAmount    = edited.addlChargesAmount,
                        confirmationByName   = edited.confirmationByName,
                        confirmationByMobile = edited.confirmationByMobile,
                        executiveName        = edited.executiveName,
                        collectionUpdate     = edited.collectionUpdate,
                        remark               = edited.remark,
                        billingAction        = edited.billingAction,
                        holdUntil            = edited.holdUntil.ifBlank { null },
                        holdDays             = edited.holdDays.takeIf { d -> d > 0 }
                    )
                )
                if (resp.isSuccessful) {
                    _ui.update { st ->
                        st.copy(
                            saving = false,
                            editing = null,
                            savedMsg = "Saved",
                            items = st.items.map { if (it.id == edited.id) edited else it }
                        )
                    }
                } else {
                    _ui.update { it.copy(saving = false, errorMsg = "Could not save. Try again.") }
                }
            }.onFailure {
                _ui.update { it.copy(saving = false, errorMsg = "Network error. Try again.") }
            }
        }
    }
}
