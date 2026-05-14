package com.vkenterprises.vras.data.repository

import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.models.SearchResult
import org.json.JSONObject

sealed class SearchResult2 {
    data class Success(val data: List<SearchResult>) : SearchResult2()
    data class SubscriptionExpired(val msg: String = "Subscription expired.") : SearchResult2()
    data class AppStopped(val msg: String) : SearchResult2()
    data class Blacklisted(val msg: String) : SearchResult2()
    data class Inactive(val msg: String) : SearchResult2()
    data class Error(val message: String) : SearchResult2()
}

class SearchRepository {
    private val api = ApiClient.api

    suspend fun searchRc(last4: String, userId: Long): SearchResult2 =
        runCatching {
            val resp = api.searchRc(last4, userId)
            mapSearchResponse(resp)
        }.getOrElse { SearchResult2.Error(it.message ?: "Network error") }

    suspend fun searchChassis(last5: String, userId: Long): SearchResult2 =
        runCatching {
            val resp = api.searchChassis(last5, userId)
            mapSearchResponse(resp)
        }.getOrElse { SearchResult2.Error(it.message ?: "Network error") }

    private fun mapSearchResponse(resp: retrofit2.Response<com.vkenterprises.vras.data.models.SearchResponse>): SearchResult2 {
        if (resp.isSuccessful) return SearchResult2.Success(resp.body()?.results ?: emptyList())
        val body = resp.errorBody()?.string()
        val reason = runCatching { body?.let { org.json.JSONObject(it).optString("message") } }.getOrNull()
        return when {
            resp.code() == 402                   -> SearchResult2.SubscriptionExpired()
            resp.code() == 403 && reason == "app_stopped"  ->
                SearchResult2.AppStopped("Your app has been stopped by admin. Please contact agency to start app.")
            resp.code() == 403 && reason == "blacklisted"  ->
                SearchResult2.Blacklisted("You have been blocked by the agency. Please contact the agency for assistance.")
            resp.code() == 403 && reason == "inactive"     ->
                SearchResult2.Inactive("Your account is inactive. Please contact agency.")
            else -> SearchResult2.Error(reason ?: "Search failed (${resp.code()})")
        }
    }
}
