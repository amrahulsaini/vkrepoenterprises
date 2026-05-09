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
    val syncTotal: Long               = 0L
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
        viewModelScope.launch(Dispatchers.IO) {
            triggerSync()  // Always check on app open; sync() silently skips unchanged branches
        }
    }

    fun triggerSync() {
        if (syncJob?.isActive == true) return
        syncJob = viewModelScope.launch(Dispatchers.IO) {
            runCatching {
                syncRepo.sync { p ->
                    when {
                        p.started -> _ui.update { it.copy(isSyncing = true, syncCurrent = 0L, syncTotal = p.total) }
                        p.done    -> _ui.update { it.copy(isSyncing = false) }
                        else      -> _ui.update { it.copy(syncCurrent = p.current, syncTotal = p.total) }
                    }
                }
            }
            // Always clear the banner even if sync threw an exception
            _ui.update { it.copy(isSyncing = false) }
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

    private suspend fun executeSearch(q: String, mode: SearchMode, userId: Long) {
        // Try Room first (instant)
        val local = withContext(Dispatchers.IO) {
            if (mode == SearchMode.RC) vehicleDao.searchByLast4(q)
            else vehicleDao.searchByLast5(q)
        }
        if (local.isNotEmpty()) {
            _ui.update { it.copy(results = local.map { it.toSearchResult() }, errorMsg = null) }
            return
        }

        // Server fallback (no local data yet)
        val result = withContext(Dispatchers.IO) {
            if (mode == SearchMode.RC) serverRepo.searchRc(q, userId)
            else serverRepo.searchChassis(q, userId)
        }
        _ui.update {
            when (result) {
                is SearchResult2.Success           -> it.copy(results = result.data, errorMsg = null)
                is SearchResult2.SubscriptionExpired -> it.copy(subscriptionExpired = true)
                is SearchResult2.Error             -> it.copy(errorMsg = result.message)
            }
        }
    }
}

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
