package com.vkenterprises.crmrs.data.repository

import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.*
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
    }.getOrElse { AuthResult.Error(com.vkenterprises.crmrs.utils.NetworkError.friendly(it)) }

    suspend fun login(request: LoginRequest): AuthResult<AuthResponse> = runCatching {
        val resp = api.login(request)
        when {
            resp.isSuccessful && resp.body() != null -> AuthResult.Success(resp.body()!!)
            resp.code() == 400 || resp.code() == 403 -> {
                val body = resp.errorBody()?.string() ?: ""
                val reason = when {
                    body.contains("kyc_failed")       -> "kyc_failed"
                    body.contains("kyc_pending")      -> "kyc_pending"
                    body.contains("app_stopped")      -> "app_stopped"
                    body.contains("blacklisted")      -> "blacklisted"
                    body.contains("\"inactive\"")     -> "inactive"
                    body.contains("device_mismatch")  -> "device_mismatch"
                    body.contains("otp_required")     -> "otp_required"
                    body.contains("pending_approval") -> "pending_approval"
                    // Anything unrecognized (transient proxy/WAF error, a body
                    // shape we don't know about, etc.) must NOT be silently
                    // relabeled as "pending admin approval" — that previously
                    // made unrelated failures look identical to a real
                    // approval-pending account.
                    else                              -> ""
                }
                val serverMsg = runCatching {
                    org.json.JSONObject(body).optString("message", "")
                }.getOrNull() ?: ""
                AuthResult.Error(serverMsg.ifBlank {
                    when (reason) {
                        "kyc_failed"       -> "Your KYC was rejected. Please re-submit your documents."
                        "kyc_pending"      -> "Your KYC is under review. Please wait for verification."
                        "app_stopped"      -> "Your app has been stopped by admin. Please contact agency to start app."
                        "blacklisted"      -> "You have been blocked by the agency. Please contact the agency for assistance."
                        "inactive"         -> "Your account is inactive. Please contact agency."
                        "device_mismatch"  -> "This account is registered on another device.\nAsk admin to reset your device ID."
                        "otp_required"     -> "Please verify your mobile number with the OTP first."
                        "pending_approval" -> "Your account is pending admin approval.\nPlease wait."
                        else               -> "Login failed. Please try again."
                    }
                }, reason)
            }
            resp.code() == 404 -> AuthResult.Error("Mobile number not registered.", "not_found")
            else -> AuthResult.Error("Login failed. Please try again.")
        }
    }.getOrElse { AuthResult.Error(com.vkenterprises.crmrs.utils.NetworkError.friendly(it)) }

    suspend fun checkMobileRegistered(mobile: String, slug: String): AuthResult<Boolean> = runCatching {
        val resp = api.checkMobile(mapOf("mobile" to mobile, "slug" to slug))
        if (resp.isSuccessful) AuthResult.Success(resp.body()?.get("registered") == true)
        else AuthResult.Error(parseError(resp))
    }.getOrElse { AuthResult.Error(com.vkenterprises.crmrs.utils.NetworkError.friendly(it)) }

    suspend fun sendOtp(mobile: String): AuthResult<String> = runCatching {
        val resp = api.otpSend(mapOf("mobile" to mobile))
        if (resp.isSuccessful) AuthResult.Success("OTP sent.")
        else AuthResult.Error(parseError(resp))
    }.getOrElse { AuthResult.Error(com.vkenterprises.crmrs.utils.NetworkError.friendly(it)) }

    suspend fun verifyOtp(mobile: String, otp: String): AuthResult<String> = runCatching {
        val resp = api.otpVerify(mapOf("mobile" to mobile, "otp" to otp))
        if (resp.isSuccessful) AuthResult.Success("Verified.")
        else AuthResult.Error(parseError(resp))
    }.getOrElse { AuthResult.Error(com.vkenterprises.crmrs.utils.NetworkError.friendly(it)) }

    suspend fun getAgencies(): AuthResult<List<AgencyListItem>> = runCatching {
        val resp = api.getAgencies()
        if (resp.isSuccessful && resp.body() != null) AuthResult.Success(resp.body()!!)
        else AuthResult.Error(parseError(resp))
    }.getOrElse { AuthResult.Error(com.vkenterprises.crmrs.utils.NetworkError.friendly(it)) }

    private fun <T> parseError(resp: Response<T>): String =
        resp.errorBody()?.string()?.let {
            Regex("\"message\":\"([^\"]+)\"").find(it)?.groupValues?.getOrNull(1)
        } ?: "Error ${resp.code()}"
}
