package com.vkenterprises.vras.data.repository

import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.models.*
import retrofit2.Response

sealed class AuthResult<out T> {
    data class Success<T>(val data: T) : AuthResult<T>()
    data class Error(val message: String, val reason: String = "") : AuthResult<Nothing>()
}

class AuthRepository {
    private val api = ApiClient.api

    suspend fun register(request: RegisterRequest): AuthResult<String> = runCatching {
        val resp = api.register(request)
        if (resp.isSuccessful) AuthResult.Success("Registered! Waiting for admin approval.")
        else AuthResult.Error(parseError(resp))
    }.getOrElse { AuthResult.Error(it.message ?: "Network error") }

    suspend fun login(request: LoginRequest): AuthResult<AuthResponse> = runCatching {
        val resp = api.login(request)
        when {
            resp.isSuccessful && resp.body() != null -> AuthResult.Success(resp.body()!!)
            resp.code() == 403 -> {
                val body = resp.errorBody()?.string() ?: ""
                val reason = when {
                    body.contains("app_stopped")     -> "app_stopped"
                    body.contains("blacklisted")     -> "blacklisted"
                    body.contains("\"inactive\"")    -> "inactive"
                    body.contains("device_mismatch") -> "device_mismatch"
                    else                             -> "pending_approval"
                }
                val serverMsg = runCatching {
                    org.json.JSONObject(body).optString("message", "")
                }.getOrNull() ?: ""
                AuthResult.Error(serverMsg.ifBlank {
                    when (reason) {
                        "app_stopped"     -> "Your app has been stopped by admin. Please contact agency to start app."
                        "blacklisted"     -> "You have been blocked by the agency. Please contact the agency for assistance."
                        "inactive"        -> "Your account is inactive. Please contact agency."
                        "device_mismatch" -> "This account is registered on another device.\nAsk admin to reset your device ID."
                        else              -> "Your account is pending admin approval.\nPlease wait."
                    }
                }, reason)
            }
            resp.code() == 404 -> AuthResult.Error("Mobile number not registered.", "not_found")
            else -> AuthResult.Error("Login failed. Please try again.")
        }
    }.getOrElse { AuthResult.Error(it.message ?: "Network error") }

    private fun <T> parseError(resp: Response<T>): String =
        resp.errorBody()?.string()?.let {
            Regex("\"message\":\"([^\"]+)\"").find(it)?.groupValues?.getOrNull(1)
        } ?: "Error ${resp.code()}"
}
