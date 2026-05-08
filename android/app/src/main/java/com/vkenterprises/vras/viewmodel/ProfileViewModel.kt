package com.vkenterprises.vras.viewmodel

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.models.ProfileResponse
import com.vkenterprises.vras.utils.PreferencesManager
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class ProfileUiState {
    object Loading : ProfileUiState()
    data class Success(val profile: ProfileResponse) : ProfileUiState()
    data class Error(val message: String) : ProfileUiState()
}

@HiltViewModel
class ProfileViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val prefs: PreferencesManager
) : ViewModel() {

    private val api = ApiClient.api

    private val _state = MutableStateFlow<ProfileUiState>(ProfileUiState.Loading)
    val state: StateFlow<ProfileUiState> = _state.asStateFlow()

    private val _pfpUpdating = MutableStateFlow(false)
    val pfpUpdating: StateFlow<Boolean> = _pfpUpdating.asStateFlow()

    fun load(userId: Long) = viewModelScope.launch {
        _state.value = ProfileUiState.Loading
        runCatching {
            val resp = api.getProfile(userId)
            if (resp.isSuccessful && resp.body() != null)
                _state.value = ProfileUiState.Success(resp.body()!!)
            else
                _state.value = ProfileUiState.Error("Failed to load profile (${resp.code()})")
        }.onFailure {
            _state.value = ProfileUiState.Error(it.message ?: "Network error")
        }
    }

    fun updatePfp(userId: Long, pfpBase64: String?) = viewModelScope.launch {
        _pfpUpdating.value = true
        runCatching {
            val resp = api.updatePfp(userId, mapOf("pfpBase64" to pfpBase64))
            if (resp.isSuccessful) {
                prefs.savePfp(pfpBase64)
                load(userId)
            } else {
                _state.value = ProfileUiState.Error("Failed to update photo")
            }
        }.onFailure {
            _state.value = ProfileUiState.Error(it.message ?: "Network error")
        }
        _pfpUpdating.value = false
    }
}
