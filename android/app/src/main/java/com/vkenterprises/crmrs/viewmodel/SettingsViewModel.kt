package com.vkenterprises.crmrs.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Job
import com.vkenterprises.crmrs.data.api.ApiService
import com.vkenterprises.crmrs.data.local.BranchSyncState
import com.vkenterprises.crmrs.data.local.TenantDb
import com.vkenterprises.crmrs.data.repository.SyncRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import javax.inject.Inject

@HiltViewModel
class SettingsViewModel @Inject constructor(
    private val db: TenantDb,
    private val syncRepo: SyncRepository,
    private val api: ApiService
) : ViewModel() {

    private val vehicleDao get() = db.vehicleCacheDao()

    data class UiState(
        val roomCount: Long            = 0L,
        val syncLogs: List<BranchSyncState> = emptyList(),
        val serverVehicleRecords: Long = 0L,
        val serverRcRecords: Long      = 0L,
        val serverChassisRecords: Long = 0L,
        val isLoading: Boolean         = true,
        val isSyncing: Boolean         = false,
        val syncPaused: Boolean        = false,
        val syncProgress: String?      = null,
        val statsError: String?        = null,
        val showLogs: Boolean          = false,
        val syncHasUpdates: Boolean    = false,
        val syncCompleted: Boolean     = false
    )

    private val _ui = MutableStateFlow(UiState())
    val ui: StateFlow<UiState> = _ui.asStateFlow()

    init { loadAll() }

    fun loadAll() {
        viewModelScope.launch(Dispatchers.IO) {
            _ui.update { it.copy(isLoading = true) }
            val roomCount    = vehicleDao.count()
            val syncLogs     = syncRepo.getSyncLogs()
            val statsResp    = runCatching { api.getStats() }.getOrNull()
            val body         = statsResp?.body()
            val hasUpdates   = syncRepo.hasUpdates()
            _ui.update {
                it.copy(
                    roomCount             = roomCount,
                    syncLogs              = syncLogs,
                    serverVehicleRecords  = body?.vehicleRecords ?: 0L,
                    serverRcRecords       = body?.rcRecords ?: 0L,
                    serverChassisRecords  = body?.chassisRecords ?: 0L,
                    isLoading             = false,
                    statsError            = if (body == null) "Could not reach server" else null,
                    syncHasUpdates        = hasUpdates,
                    syncCompleted         = if (hasUpdates) false else it.syncCompleted
                )
            }
        }
    }

    private var syncJob: Job? = null

    fun pauseSync() {
        syncJob?.cancel()
        syncJob = null
        _ui.update { it.copy(isSyncing = false, syncPaused = true, syncProgress = "Paused — tap Sync to resume") }
        viewModelScope.launch(Dispatchers.IO) { loadAll() }
    }

    fun smartSync() {
        if (_ui.value.isSyncing) return
        syncJob = viewModelScope.launch(Dispatchers.IO) {
            _ui.update { it.copy(isSyncing = true, syncPaused = false, syncProgress = "Checking for updates…", syncCompleted = false) }
            var success = false
            runCatching {
                syncRepo.sync { p ->
                    _ui.update {
                        it.copy(syncProgress = when {
                            p.started -> "Downloading ${p.total} records…"
                            p.done    -> "Done!"
                            else      -> "Synced ${p.current} / ${p.total}…"
                        })
                    }
                }
                success = true
            }.onFailure { e ->
                _ui.update { it.copy(syncProgress = com.vkenterprises.crmrs.utils.NetworkError.friendly(e)) }
            }
            val stillPending = runCatching { syncRepo.hasUpdates() }.getOrDefault(false)
            _ui.update {
                it.copy(isSyncing = false, syncCompleted = success && !stillPending,
                    syncHasUpdates = stillPending)
            }
            loadAll()
        }
    }

    fun forceSync() {
        if (_ui.value.isSyncing) return
        syncJob = viewModelScope.launch(Dispatchers.IO) {
            _ui.update { it.copy(isSyncing = true, syncPaused = false, syncProgress = "Starting…", syncCompleted = false) }
            var success = false
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
                success = true
            }.onFailure { e ->
                _ui.update { it.copy(syncProgress = com.vkenterprises.crmrs.utils.NetworkError.friendly(e)) }
            }
            val stillPending = runCatching { syncRepo.hasUpdates() }.getOrDefault(false)
            _ui.update {
                it.copy(isSyncing = false, syncCompleted = success && !stillPending,
                    syncHasUpdates = stillPending)
            }
            loadAll()
        }
    }

    fun toggleLogs() {
        _ui.update { it.copy(showLogs = !it.showLogs) }
    }
}
