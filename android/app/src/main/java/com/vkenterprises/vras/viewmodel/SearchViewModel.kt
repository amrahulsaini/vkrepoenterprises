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
    val query: String           = "",
    val mode: SearchMode        = SearchMode.RC,
    val results: List<SearchResult> = emptyList(),
    val selectedResult: SearchResult? = null,
    val isLoading: Boolean      = false,
    val errorMsg: String?       = null,
    val subscriptionExpired: Boolean = false,
    val hint: String            = ""
)

@HiltViewModel
class SearchViewModel @Inject constructor() : ViewModel() {

    private val repo = SearchRepository()

    private val _ui = MutableStateFlow(SearchUiState())
    val ui: StateFlow<SearchUiState> = _ui.asStateFlow()

    private var searchJob: Job? = null
    private var _userId: Long = -1L

    fun setUser(userId: Long) { _userId = userId }

    fun onQueryChange(q: String, userId: Long) {
        _userId = userId
        val mode = _ui.value.mode
        val requiredLen = if (mode == SearchMode.RC) 4 else 5
        _ui.update { it.copy(query = q, errorMsg = null) }

        searchJob?.cancel()
        when {
            q.length == requiredLen -> {
                searchJob = viewModelScope.launch {
                    executeSearch(q, mode, userId)
                }
            }
            q.isEmpty() -> _ui.update { it.copy(results = emptyList(), hint = "") }
            q.length > requiredLen -> {
                // keep existing results, just update query display
            }
        }
    }

    fun setMode(mode: SearchMode) {
        _ui.update { it.copy(mode = mode, query = "", results = emptyList(), hint = "") }
    }

    fun selectResult(result: SearchResult) {
        _ui.update { it.copy(selectedResult = result) }
    }

    private suspend fun executeSearch(q: String, mode: SearchMode, userId: Long) {
        _ui.update { it.copy(isLoading = true) }
        val result = if (mode == SearchMode.RC)
            repo.searchRc(q.uppercase(), userId)
        else
            repo.searchChassis(q.uppercase(), userId)

        _ui.update {
            when (result) {
                is SearchResult2.Success ->
                    it.copy(
                        isLoading = false,
                        results   = result.data,
                        hint      = if (result.data.isEmpty()) "No results for \"$q\""
                                    else "${result.data.size} result(s)"
                    )
                is SearchResult2.SubscriptionExpired ->
                    it.copy(isLoading = false, subscriptionExpired = true)
                is SearchResult2.Error ->
                    it.copy(isLoading = false, errorMsg = result.message)
            }
        }
    }
}
