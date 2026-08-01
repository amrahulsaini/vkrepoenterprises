package com.vkenterprises.crmrs.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.crmrs.data.local.TenantDb
import com.vkenterprises.crmrs.data.local.VehicleCache
import com.vkenterprises.crmrs.data.models.SearchResult
import com.vkenterprises.crmrs.data.repository.SearchRepository
import com.vkenterprises.crmrs.data.repository.SearchResult2
import com.vkenterprises.crmrs.data.repository.SyncRepository
import com.vkenterprises.crmrs.utils.PreferencesManager
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import javax.inject.Inject

enum class SearchMode { RC, CHASSIS }

data class SearchUiState(
    val inputText: String             = "",
    val prefixInput: String           = "",
    val lastQuery: String             = "",
    val mode: SearchMode              = SearchMode.RC,
    val results: List<SearchResult>   = emptyList(),
    val allResults: List<SearchResult> = emptyList(),
    val selectedResult: SearchResult? = null,
    val fullRecord: SearchResult?     = null,
    val vehicleBranches: List<SearchResult> = emptyList(),
    val errorMsg: String?             = null,
    val isSearching: Boolean          = false,
    val subscriptionExpired: Boolean  = false,
    val appStopped: Boolean           = false,
    val appStoppedMsg: String         = "",
    val blacklisted: Boolean          = false,
    val blacklistedMsg: String        = "",
    val inactive: Boolean             = false,
    val inactiveMsg: String           = "",
    val isSyncing: Boolean            = false,
    val syncCurrent: Long             = 0L,
    val syncTotal: Long               = 0L,
    val syncHasUpdates: Boolean       = false,
    val syncCompleted: Boolean        = false,
    val onlineOnly: Boolean           = true,
    val showHyphens: Boolean          = true,
    val twoColumnView: Boolean        = true,
    val actionType: String            = "confirm",
    val offlineCount: Long            = 0L
)

@HiltViewModel
class SearchViewModel @Inject constructor(
    private val db: TenantDb,
    private val syncRepo: SyncRepository,
    private val prefs: PreferencesManager
) : ViewModel() {

    private val vehicleDao get() = db.vehicleCacheDao()

    private val serverRepo = SearchRepository()

    private val _ui = MutableStateFlow(SearchUiState())
    val ui: StateFlow<SearchUiState> = _ui.asStateFlow()

    private var searchJob: Job? = null
    private var syncJob: Job? = null

    val requiredLen get() = if (_ui.value.mode == SearchMode.RC) 4 else 5

    init {
        viewModelScope.launch {
            prefs.showHyphens.collect { v -> _ui.update { it.copy(showHyphens = v) } }
        }
        viewModelScope.launch(Dispatchers.IO) {
            val hasUpdates = runCatching { syncRepo.hasUpdates() }.getOrDefault(false)
            _ui.update { it.copy(syncHasUpdates = hasUpdates) }
            refreshOfflineCount()
        }
        viewModelScope.launch(Dispatchers.IO) {
            while (true) {
                kotlinx.coroutines.delay(60_000L)
                val hasUpdates = runCatching { syncRepo.hasUpdates() }.getOrDefault(false)
                _ui.update { it.copy(syncHasUpdates = hasUpdates) }
                refreshOfflineCount()
            }
        }
    }

    suspend fun refreshOfflineCount() {
        val n = runCatching { vehicleDao.count() }.getOrDefault(0L)
        _ui.update { it.copy(offlineCount = n) }
    }

    fun triggerSync() {
        if (syncJob?.isActive == true) return
        syncJob = viewModelScope.launch(Dispatchers.IO) {
            _ui.update { it.copy(syncCompleted = false) }
            var success = false
            runCatching {
                syncRepo.sync { p -> handleProgress(p) }
                success = true
            }
            _ui.update { it.copy(isSyncing = false, syncCompleted = success, syncHasUpdates = false) }
        }
    }

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
        val mode = _ui.value.mode
        val len  = if (mode == SearchMode.RC) 4 else 5
        val capped = text.filter { it.isDigit() }.take(len)
        _ui.update { it.copy(inputText = capped, errorMsg = null) }
        if (capped.length == len) {
            val prefix = if (mode == SearchMode.RC) _ui.value.prefixInput else ""
            searchJob?.cancel()
            _ui.update { it.copy(inputText = "", isSearching = true, errorMsg = null) }
            searchJob = viewModelScope.launch { delay(90); executeSearch(capped, mode, userId, prefix) }
        }
    }

    fun onPrefixChange(text: String) {
        val p = text.uppercase().filter { it.isLetter() }.take(3)
        _ui.update { it.copy(prefixInput = p) }
        reFilterByPrefix(p)
    }

    private fun reFilterByPrefix(prefix: String) {
        val s = _ui.value
        if (s.mode != SearchMode.RC) return
        val filtered = s.allResults.filter { it.vehicleNo.isValidRc() && matchesPrefix(it.vehicleNo, prefix) }
        _ui.update { it.copy(results = filtered.bestPerVehicle(SearchMode.RC)) }
    }

    private fun matchesPrefix(vehicleNo: String, prefix: String): Boolean {
        if (prefix.isBlank()) return true
        return vehicleNo.filter { it.isLetterOrDigit() }.uppercase().startsWith(prefix.uppercase())
    }

    fun setMode(mode: SearchMode) {
        searchJob?.cancel()
        _ui.update { it.copy(mode = mode, inputText = "", prefixInput = "", results = emptyList(), allResults = emptyList(), errorMsg = null) }
    }

    fun selectResult(result: SearchResult) {
        _ui.update { it.copy(selectedResult = result, fullRecord = null, vehicleBranches = emptyList()) }
    }

    fun loadVehicleBranches(userId: Long) {
        val current = _ui.value.selectedResult ?: return
        val key = current.vehicleNo.trim().ifBlank { current.chassisNo.trim() }
        if (key.isBlank()) return
        viewModelScope.launch {
            val rows = withContext(Dispatchers.IO) { serverRepo.getVehicleBranches(key, userId) }
            if (rows.isNotEmpty()) _ui.update { it.copy(vehicleBranches = rows) }
        }
    }

    fun fetchFullRecord(id: Long, userId: Long) {
        viewModelScope.launch {
            val rec = withContext(Dispatchers.IO) { serverRepo.getRecord(id, userId) }
            if (rec != null) _ui.update { it.copy(fullRecord = rec) }
        }
    }

    fun setOnlineOnly(v: Boolean) {
        _ui.update { it.copy(onlineOnly = v, results = emptyList(), allResults = emptyList(), errorMsg = null, inputText = "") }
    }

    fun setTwoColumnView(v: Boolean) {
        _ui.update { it.copy(twoColumnView = v) }
    }

    fun setShowHyphens(v: Boolean) {
        _ui.update { it.copy(showHyphens = v) }
        viewModelScope.launch { prefs.setShowHyphens(v) }
    }

    fun setActionType(type: String) {
        _ui.update { it.copy(actionType = type) }
    }

    fun resetBlockedStates() {
        _ui.update { it.copy(
            appStopped = false, appStoppedMsg = "",
            blacklisted = false, blacklistedMsg = "",
            inactive = false, inactiveMsg = "",
            subscriptionExpired = false
        )}
    }

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
                    _ui.update { it.copy(selectedResult = match, results = result.data, allResults = result.data) }
                }
            }
        }
    }

    private suspend fun executeSearch(q: String, mode: SearchMode, userId: Long, statePrefix: String = "") {
        _ui.update { it.copy(isSearching = true, errorMsg = null) }

        if (!_ui.value.onlineOnly) {
            val local = withContext(Dispatchers.IO) {
                if (mode == SearchMode.RC) vehicleDao.searchByLast4(q)
                else vehicleDao.searchByLast5(q)
            }
            val all = if (mode == SearchMode.RC)
                local.filter { it.vehicleNo.isValidRc() }
            else
                local
            val full     = all.map { it.toSearchResult() }
            val filtered = if (mode == SearchMode.RC)
                full.filter { matchesPrefix(it.vehicleNo, statePrefix) } else full
            val unique = filtered.bestPerVehicle(mode)
            _ui.update { it.copy(results = unique, allResults = full, lastQuery = q, errorMsg = null, isSearching = false) }
            return
        }

        val result = try {
            withContext(Dispatchers.IO) {
                withTimeout(20_000) {
                    if (mode == SearchMode.RC) serverRepo.searchRc(q, userId)
                    else serverRepo.searchChassis(q, userId)
                }
            }
        } catch (e: TimeoutCancellationException) {
            SearchResult2.Error("Search timed out — please check your connection and try again.")
        }
        _ui.update {
            when (result) {
                is SearchResult2.Success -> {
                    val full = if (mode == SearchMode.RC)
                        result.data.filter { it.vehicleNo.isValidRc() }.sortedBy { it.vehicleNo }
                    else
                        result.data.sortedBy { it.chassisNo }
                    val filtered = if (mode == SearchMode.RC)
                        full.filter { matchesPrefix(it.vehicleNo, statePrefix) } else full
                    val unique = filtered.bestPerVehicle(mode)
                    it.copy(results = unique, allResults = full, lastQuery = q, errorMsg = null, isSearching = false)
                }
                is SearchResult2.SubscriptionExpired -> it.copy(subscriptionExpired = true, isSearching = false)
                is SearchResult2.AppStopped          -> it.copy(appStopped = true, appStoppedMsg = result.msg, isSearching = false)
                is SearchResult2.Blacklisted         -> it.copy(blacklisted = true, blacklistedMsg = result.msg, isSearching = false)
                is SearchResult2.Inactive            -> it.copy(inactive = true, inactiveMsg = result.msg, isSearching = false)
                is SearchResult2.Error               -> it.copy(errorMsg = result.message, isSearching = false)
            }
        }
    }

    fun clearResults() {
        searchJob?.cancel()
        _ui.update { it.copy(results = emptyList(), allResults = emptyList(), lastQuery = "", inputText = "", prefixInput = "", errorMsg = null, isSearching = false) }
    }
}

private val RC_REGEX = Regex(
    "^([A-Z]{2}[0-9]{2}[A-Z]{1,3}[0-9]{4}|[A-Z]{2}[0-9]{5,7}|[0-9]{2}BH[0-9]{4}[A-Z]{1,2})$"
)
private fun String.isValidRc() = replace(Regex("[^A-Z0-9]"), "").uppercase().matches(RC_REGEX)

private fun isFilled(s: String?): Boolean = !s.isNullOrBlank()

private fun SearchResult.completenessScore(): Int = listOf(
    engineNo, model, agreementNo, customerName, customerContact, customerAddress,
    region, area, bucket, gv, od, seasoning, tbrFlag, sec9, sec17,
    level1, level1Contact, level2, level2Contact, level3, level3Contact, level4, level4Contact,
    senderMail1, senderMail2, executiveName, pos, toss, remark
).count { isFilled(it) }

private fun String.dedupKey() = replace(Regex("[^A-Za-z0-9]"), "").uppercase()

private fun List<SearchResult>.bestPerVehicle(mode: SearchMode): List<SearchResult> {
    val keyOf: (SearchResult) -> String =
        if (mode == SearchMode.RC) { r -> r.vehicleNo.dedupKey() } else { r -> r.chassisNo.dedupKey() }
    return groupBy(keyOf).values.map { group -> group.maxByOrNull { it.completenessScore() } ?: group.first() }
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
