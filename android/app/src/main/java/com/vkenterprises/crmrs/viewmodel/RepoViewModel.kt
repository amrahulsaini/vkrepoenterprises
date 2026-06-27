package com.vkenterprises.crmrs.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.HeadOffice
import com.vkenterprises.crmrs.data.models.RepoLetterSettings
import com.vkenterprises.crmrs.data.models.SaveRepoSettingsRequest
import com.vkenterprises.crmrs.data.models.SearchResult
import com.vkenterprises.crmrs.utils.NetworkError
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import javax.inject.Inject

enum class RepoType { PRE, POST }

enum class RepoFlow { LETTER, BILLING }

data class RepoUiState(
    val headOffices: List<HeadOffice>       = emptyList(),
    val loadingHeadOffices: Boolean         = false,
    val headOfficeError: String?            = null,

    val selectedHeadOffice: HeadOffice?     = null,

    val mode: SearchMode                    = SearchMode.RC,
    val inputText: String                   = "",
    val results: List<SearchResult>         = emptyList(),
    val lastQuery: String                   = "",
    val isSearching: Boolean                = false,
    val searchError: String?                = null,

    val selectedRecord: SearchResult?       = null,
    val loadingRecord: Boolean              = false,

    val settings: RepoLetterSettings?       = null,
    val uploadingLogo: Boolean              = false,

    val flowMode: RepoFlow                  = RepoFlow.LETTER,
    val billingSettings: com.vkenterprises.crmrs.data.models.BillingSettings? = null
)

@HiltViewModel
class RepoViewModel @Inject constructor() : ViewModel() {

    private val _ui = MutableStateFlow(RepoUiState())
    val ui: StateFlow<RepoUiState> = _ui.asStateFlow()

    private val api get() = ApiClient.api
    private var searchJob: Job? = null

    private val requiredLen get() = if (_ui.value.mode == SearchMode.RC) 4 else 5

    fun setFlow(flow: RepoFlow) {
        _ui.update { it.copy(flowMode = flow) }
    }

    fun loadBillingSettings(userId: Long) {
        viewModelScope.launch {
            val s = withContext(Dispatchers.IO) {
                runCatching { api.getBillingSettings(userId).body() }.getOrNull()
            }
            _ui.update { it.copy(billingSettings = s ?: com.vkenterprises.crmrs.data.models.BillingSettings()) }
        }
    }

    suspend fun saveBillingSettings(userId: Long, req: com.vkenterprises.crmrs.data.models.BillingSettings) {
        val existingLogo = _ui.value.billingSettings?.logoUrl
        withContext(Dispatchers.IO) { runCatching { api.saveBillingSettings(userId, req) } }
        _ui.update { it.copy(billingSettings = req.copy(logoUrl = req.logoUrl ?: existingLogo)) }
    }

    fun uploadBillingLogo(userId: Long, imageBase64: String) {
        _ui.update { it.copy(uploadingLogo = true) }
        viewModelScope.launch {
            val url = withContext(Dispatchers.IO) {
                runCatching {
                    val resp = api.saveBillingLogo(userId, com.vkenterprises.crmrs.data.models.UploadRepoLogoRequest(imageBase64))
                    if (resp.isSuccessful) resp.body()?.get("logoUrl") as? String else null
                }.getOrNull()
            }
            _ui.update {
                val s = it.billingSettings ?: com.vkenterprises.crmrs.data.models.BillingSettings()
                it.copy(uploadingLogo = false, billingSettings = s.copy(logoUrl = url ?: s.logoUrl))
            }
        }
    }

    fun loadHeadOffices(userId: Long) {
        _ui.update { it.copy(loadingHeadOffices = true, headOfficeError = null) }
        viewModelScope.launch {
            val result = withContext(Dispatchers.IO) {
                runCatching { api.getHeadOffices(userId) }
            }
            result.onSuccess { resp ->
                if (resp.isSuccessful) {
                    _ui.update { it.copy(headOffices = resp.body() ?: emptyList(), loadingHeadOffices = false) }
                } else {
                    _ui.update { it.copy(loadingHeadOffices = false,
                        headOfficeError = "Could not load head offices (${resp.code()}).") }
                }
            }.onFailure { e ->
                _ui.update { it.copy(loadingHeadOffices = false, headOfficeError = NetworkError.friendly(e)) }
            }
        }
    }

    fun selectHeadOffice(ho: HeadOffice, userId: Long) {
        searchJob?.cancel()
        _ui.update {
            it.copy(
                selectedHeadOffice = ho,
                inputText = "", results = emptyList(), lastQuery = "",
                isSearching = false, searchError = null,
                selectedRecord = null, settings = null
            )
        }
        loadSettings(ho.id, userId)
    }

    private fun loadSettings(financeId: Long, userId: Long) {
        viewModelScope.launch {
            val s = withContext(Dispatchers.IO) {
                runCatching { api.getRepoSettings(financeId, userId).body() }.getOrNull()
            }
            _ui.update { it.copy(settings = s ?: RepoLetterSettings(financeId = financeId)) }
        }
    }

    fun setMode(mode: SearchMode) {
        searchJob?.cancel()
        _ui.update { it.copy(mode = mode, inputText = "", results = emptyList(),
            lastQuery = "", isSearching = false, searchError = null) }
    }

    fun onInputChange(text: String, userId: Long) {
        val capped = text.filter { it.isDigit() }.take(requiredLen)
        _ui.update { it.copy(inputText = capped, searchError = null) }
        if (capped.length == requiredLen) {
            val q    = capped.uppercase()
            val mode = _ui.value.mode
            searchJob?.cancel()
            _ui.update { it.copy(inputText = "", isSearching = true, searchError = null) }
            searchJob = viewModelScope.launch {
                delay(90)
                executeSearch(q, mode, userId)
            }
        }
    }

    private suspend fun executeSearch(q: String, mode: SearchMode, userId: Long) {
        val financeId = _ui.value.selectedHeadOffice?.id ?: 0L
        val resp = withContext(Dispatchers.IO) {
            runCatching {
                if (mode == SearchMode.RC)
                    api.searchRcInFinance(q, userId, financeId)
                else
                    api.searchChassisInFinance(q, userId, financeId)
            }
        }
        resp.onSuccess { r ->
            if (r.isSuccessful) {
                val all = r.body()?.results ?: emptyList()
                val unique = if (mode == SearchMode.RC)
                    all.filter { it.vehicleNo.isNotBlank() }.distinctBy { it.vehicleNo }
                else
                    all.distinctBy { it.chassisNo }
                _ui.update { it.copy(results = unique, lastQuery = q, isSearching = false, searchError = null) }
            } else {
                _ui.update { it.copy(isSearching = false,
                    searchError = "Search failed (${r.code()}).", results = emptyList()) }
            }
        }.onFailure { e ->
            _ui.update { it.copy(isSearching = false, searchError = NetworkError.friendly(e)) }
        }
    }

    fun clearSearch() {
        searchJob?.cancel()
        _ui.update { it.copy(results = emptyList(), lastQuery = "", inputText = "",
            isSearching = false, searchError = null) }
    }

    fun selectVehicle(id: Long, userId: Long, onReady: () -> Unit) {
        _ui.update { it.copy(loadingRecord = true, selectedRecord = null) }
        viewModelScope.launch {
            val rec = withContext(Dispatchers.IO) {
                runCatching { api.getRecord(id, userId).body() }.getOrNull()
            }
            _ui.update { it.copy(loadingRecord = false, selectedRecord = rec) }
            if (rec != null) onReady()
        }
    }

    fun uploadLogo(userId: Long, imageBase64: String) {
        val financeId = _ui.value.selectedHeadOffice?.id ?: return
        _ui.update { it.copy(uploadingLogo = true) }
        viewModelScope.launch {
            val url = withContext(Dispatchers.IO) {
                runCatching {
                    val resp = api.saveRepoLogo(financeId, userId, com.vkenterprises.crmrs.data.models.UploadRepoLogoRequest(imageBase64))
                    if (resp.isSuccessful) resp.body()?.get("logoUrl") as? String else null
                }.getOrNull()
            }
            _ui.update {
                val s = it.settings ?: RepoLetterSettings(financeId = financeId)
                it.copy(uploadingLogo = false, settings = s.copy(logoUrl = url ?: s.logoUrl))
            }
        }
    }

    suspend fun saveSettings(userId: Long, req: SaveRepoSettingsRequest) {
        val financeId = _ui.value.selectedHeadOffice?.id ?: return
        withContext(Dispatchers.IO) {
            runCatching { api.saveRepoSettings(financeId, userId, req) }
        }
        _ui.update {
            it.copy(settings = RepoLetterSettings(
                financeId = financeId,
                agencyName = req.agencyName,
                authorizedBy = req.authorizedBy,
                policeStation = req.policeStation,
                policeAddress = req.policeAddress
            ))
        }
    }
}
