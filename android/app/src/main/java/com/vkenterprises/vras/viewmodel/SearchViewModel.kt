package com.vkenterprises.vras.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.vras.data.local.VehicleCache
import com.vkenterprises.vras.data.local.VehicleCacheDao
import com.vkenterprises.vras.data.models.SearchResult
import com.vkenterprises.vras.data.repository.SearchRepository
import com.vkenterprises.vras.data.repository.SearchResult2
import com.vkenterprises.vras.data.repository.SyncRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import javax.inject.Inject

enum class SearchMode { RC, CHASSIS }

data class SearchUiState(
    val inputText: String             = "",
    val mode: SearchMode              = SearchMode.RC,
    val results: List<SearchResult>   = emptyList(),
    val selectedResult: SearchResult? = null,
    val errorMsg: String?             = null,
    val subscriptionExpired: Boolean  = false,
    val isSyncing: Boolean            = false,
    val syncCurrent: Long             = 0L,
    val syncTotal: Long               = 0L,
    val onlineOnly: Boolean           = false,
    val twoColumnView: Boolean        = true,
    val actionType: String            = "confirm"
)

@HiltViewModel
class SearchViewModel @Inject constructor(
    private val vehicleDao: VehicleCacheDao,
    private val syncRepo: SyncRepository
) : ViewModel() {

    private val serverRepo = SearchRepository()

    private val _ui = MutableStateFlow(SearchUiState())
    val ui: StateFlow<SearchUiState> = _ui.asStateFlow()

    private var searchJob: Job? = null
    private var syncJob: Job? = null

    val requiredLen get() = if (_ui.value.mode == SearchMode.RC) 4 else 5

    init {
        // Poll every 60s while the ViewModel is alive (app open or in recent apps).
        // sync() is cheap when nothing changed — just one API call to compare branch
        // timestamps. Downloads only happen when uploadedAt or record count differs.
        viewModelScope.launch(Dispatchers.IO) {
            while (true) {
                triggerSync()
                kotlinx.coroutines.delay(60_000L)
            }
        }
    }

    fun triggerSync() {
        if (syncJob?.isActive == true) return
        syncJob = viewModelScope.launch(Dispatchers.IO) {
            runCatching {
                syncRepo.sync { p -> handleProgress(p) }
            }
            _ui.update { it.copy(isSyncing = false) }
        }
    }

    // Clears all local sync state and re-downloads everything from server
    fun forceRefresh() {
        syncJob?.cancel()
        syncJob = viewModelScope.launch(Dispatchers.IO) {
            runCatching {
                syncRepo.forceSync { p -> handleProgress(p) }
            }
            _ui.update { it.copy(isSyncing = false) }
        }
    }

    private fun handleProgress(p: SyncRepository.Progress) {
        when {
            p.started -> _ui.update { it.copy(isSyncing = true, syncCurrent = 0L, syncTotal = p.total) }
            p.done    -> _ui.update { it.copy(isSyncing = false) }
            else      -> _ui.update { it.copy(syncCurrent = p.current, syncTotal = p.total) }
        }
    }

    fun onInputChange(text: String, userId: Long) {
        val capped = text.take(requiredLen)
        _ui.update { it.copy(inputText = capped, errorMsg = null) }
        if (capped.length == requiredLen) {
            searchJob?.cancel()
            searchJob = viewModelScope.launch {
                executeSearch(capped.uppercase(), _ui.value.mode, userId)
            }
            _ui.update { it.copy(inputText = "") }
        }
    }

    fun setMode(mode: SearchMode) {
        searchJob?.cancel()
        _ui.update { it.copy(mode = mode, inputText = "", results = emptyList(), errorMsg = null) }
    }

    fun selectResult(result: SearchResult) {
        _ui.update { it.copy(selectedResult = result) }
    }

    fun setOnlineOnly(v: Boolean) {
        _ui.update { it.copy(onlineOnly = v, results = emptyList(), errorMsg = null, inputText = "") }
    }

    fun setTwoColumnView(v: Boolean) {
        _ui.update { it.copy(twoColumnView = v) }
    }

    fun setActionType(type: String) {
        _ui.update { it.copy(actionType = type) }
    }

    // For admin: current selectedResult came from local cache (most fields blank).
    // Re-fetch the same vehicle from server to get full field data.
    fun refetchSelectedFromServer(userId: Long) {
        val current = _ui.value.selectedResult ?: return
        viewModelScope.launch {
            val (q, mode) = if (current.vehicleNo.isNotBlank()) {
                val clean = current.vehicleNo.replace(Regex("[^A-Z0-9]"), "").uppercase()
                clean.takeLast(4) to SearchMode.RC
            } else {
                val clean = current.chassisNo.replace(Regex("[^A-Z0-9]"), "").uppercase()
                clean.takeLast(5) to SearchMode.CHASSIS
            }
            val result = withContext(Dispatchers.IO) {
                if (mode == SearchMode.RC) serverRepo.searchRc(q, userId)
                else serverRepo.searchChassis(q, userId)
            }
            if (result is SearchResult2.Success) {
                val match = result.data.firstOrNull {
                    it.vehicleNo == current.vehicleNo || it.chassisNo == current.chassisNo
                }
                if (match != null) {
                    _ui.update { it.copy(selectedResult = match, results = result.data) }
                }
            }
        }
    }

    private suspend fun executeSearch(q: String, mode: SearchMode, userId: Long) {
        if (!_ui.value.onlineOnly) {
            val local = withContext(Dispatchers.IO) {
                if (mode == SearchMode.RC) vehicleDao.searchByLast4(q)
                else vehicleDao.searchByLast5(q)
            }
            val filtered = if (mode == SearchMode.RC)
                local.filter { it.vehicleNo.isValidRc() }.distinctBy { it.vehicleNo }
            else
                local.distinctBy { it.chassisNo }
            if (filtered.isNotEmpty()) {
                _ui.update { it.copy(results = filtered.map { it.toSearchResult() }, errorMsg = null) }
                return
            }
        }

        // Server search (fallback or online-only)
        val result = withContext(Dispatchers.IO) {
            if (mode == SearchMode.RC) serverRepo.searchRc(q, userId)
            else serverRepo.searchChassis(q, userId)
        }
        _ui.update {
            when (result) {
                is SearchResult2.Success -> {
                    val data = if (mode == SearchMode.RC)
                        result.data.filter { it.vehicleNo.isValidRc() }.distinctBy { it.vehicleNo }.sortedBy { it.vehicleNo }
                    else
                        result.data.distinctBy { it.chassisNo }.sortedBy { it.chassisNo }
                    it.copy(results = data, errorMsg = null)
                }
                is SearchResult2.SubscriptionExpired -> it.copy(subscriptionExpired = true)
                is SearchResult2.Error               -> it.copy(errorMsg = result.message)
            }
        }
    }
}

private val RC_REGEX = Regex("^[A-Z]{2}[0-9]{2}[A-Z]{1,3}[0-9]{4}$")
private fun String.isValidRc() = replace(Regex("[^A-Z0-9]"), "").uppercase().matches(RC_REGEX)

private fun VehicleCache.toSearchResult() = SearchResult(
    id = id, vehicleNo = vehicleNo, chassisNo = chassisNo, engineNo = engineNo,
    model = model, agreementNo = "", customerName = customerName,
    customerContact = "", customerAddress = "", financer = "", branchName = "",
    firstContact = "", secondContact = "", thirdContact = "", address = "",
    region = "", area = "", bucket = "", gv = "", od = "", seasoning = "",
    tbrFlag = "", sec9 = "", sec17 = "", level1 = "", level1Contact = "",
    level2 = "", level2Contact = "", level3 = "", level3Contact = "",
    level4 = "", level4Contact = "", senderMail1 = "", senderMail2 = "",
    executiveName = "", pos = "", toss = "", remark = "", branchFromExcel = "",
    createdOn = ""
)
