package com.vkenterprises.vras.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.vras.data.models.SearchResult
import com.vkenterprises.vras.data.repository.SearchRepository
import com.vkenterprises.vras.data.repository.SearchResult2
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import javax.inject.Inject

enum class SearchMode { RC, CHASSIS }

data class SearchUiState(
    val inputText: String           = "",      // what shows in the text field
    val mode: SearchMode            = SearchMode.RC,
    val results: List<SearchResult> = emptyList(),
    val selectedResult: SearchResult? = null,
    val errorMsg: String?           = null,
    val subscriptionExpired: Boolean = false
)

@HiltViewModel
class SearchViewModel @Inject constructor() : ViewModel() {

    private val repo = SearchRepository()

    private val _ui = MutableStateFlow(SearchUiState())
    val ui: StateFlow<SearchUiState> = _ui.asStateFlow()

    private var searchJob: Job? = null

    val requiredLen get() = if (_ui.value.mode == SearchMode.RC) 4 else 5

    fun onInputChange(text: String, userId: Long) {
        // Cap at requiredLen — don't allow more
        val capped = text.take(requiredLen)
        _ui.update { it.copy(inputText = capped, errorMsg = null) }

        if (capped.length == requiredLen) {
            searchJob?.cancel()
            searchJob = viewModelScope.launch {
                executeSearch(capped.uppercase(), _ui.value.mode, userId)
            }
            // Auto-clear the input field so user can type again immediately
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
        val result = if (mode == SearchMode.RC)
            repo.searchRc(q, userId)
        else
            repo.searchChassis(q, userId)

        _ui.update {
            when (result) {
                is SearchResult2.Success ->
                    it.copy(results = result.data, errorMsg = null)
                is SearchResult2.SubscriptionExpired ->
                    it.copy(subscriptionExpired = true)
                is SearchResult2.Error ->
                    it.copy(errorMsg = result.message)
            }
        }
    }
}
