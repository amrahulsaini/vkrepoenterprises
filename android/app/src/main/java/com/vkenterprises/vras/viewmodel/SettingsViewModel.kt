package com.vkenterprises.vras.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.vras.data.api.ApiService
import com.vkenterprises.vras.data.local.BranchSyncState
import com.vkenterprises.vras.data.local.VehicleCacheDao
import com.vkenterprises.vras.data.repository.SyncRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import javax.inject.Inject

@HiltViewModel
class SettingsViewModel @Inject constructor(
    private val vehicleDao: VehicleCacheDao,
    private val syncRepo: SyncRepository,
    private val api: ApiService
) : ViewModel() {

    data class UiState(
        val roomCount: Long            = 0L,
        val syncLogs: List<BranchSyncState> = emptyList(),
        val serverVehicleRecords: Long = 0L,
        val serverRcRecords: Long      = 0L,
        val serverChassisRecords: Long = 0L,
        val isLoading: Boolean         = true,
        val isSyncing: Boolean         = false,
        val syncProgress: String?      = null,
        val statsError: String?        = null,
        val showLogs: Boolean          = false
    )

    private val _ui = MutableStateFlow(UiState())
    val ui: StateFlow<UiState> = _ui.asStateFlow()

    init { loadAll() }

    fun loadAll() {
        viewModelScope.launch(Dispatchers.IO) {
            _ui.update { it.copy(isLoading = true) }
            val roomCount = vehicleDao.count()
            val syncLogs  = syncRepo.getSyncLogs()
            val statsResp = runCatching { api.getStats() }.getOrNull()
            val body      = statsResp?.body()
            _ui.update {
                it.copy(
                    roomCount             = roomCount,
                    syncLogs              = syncLogs,
                    serverVehicleRecords  = body?.vehicleRecords ?: 0L,
                    serverRcRecords       = body?.rcRecords ?: 0L,
                    serverChassisRecords  = body?.chassisRecords ?: 0L,
                    isLoading             = false,
                    statsError            = if (body == null) "Could not reach server" else null
                )
            }
        }
    }

    fun forceSync() {
        if (_ui.value.isSyncing) return
        viewModelScope.launch(Dispatchers.IO) {
            _ui.update { it.copy(isSyncing = true, syncProgress = "Starting…") }
            runCatching {
                syncRepo.forceSync { p ->
                    _ui.update {
                        it.copy(syncProgress = when {
                            p.started -> "Downloading ${p.total} records…"
                            p.done    -> "Sync complete!"
                            else      -> "Synced ${p.current} / ${p.total}…"
                        })
                    }
                }
            }.onFailure { e ->
                _ui.update { it.copy(syncProgress = "Error: ${e.message}") }
            }
            _ui.update { it.copy(isSyncing = false) }
            loadAll()
        }
    }

    fun toggleLogs() {
        _ui.update { it.copy(showLogs = !it.showLogs) }
    }
}
