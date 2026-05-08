package com.vkenterprises.vras.data.repository

import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.models.SearchResult

sealed class SearchResult2 {
    data class Success(val data: List<SearchResult>) : SearchResult2()
    data class SubscriptionExpired(val msg: String = "Subscription expired.") : SearchResult2()
    data class Error(val message: String) : SearchResult2()
}

class SearchRepository {
    private val api = ApiClient.api

    suspend fun searchRc(last4: String, userId: Long): SearchResult2 =
        runCatching {
            val resp = api.searchRc(last4, userId)
            when {
                resp.isSuccessful -> SearchResult2.Success(resp.body()?.results ?: emptyList())
                resp.code() == 402 -> SearchResult2.SubscriptionExpired()
                else -> SearchResult2.Error("Search failed: ${resp.code()}")
            }
        }.getOrElse { SearchResult2.Error(it.message ?: "Network error") }

    suspend fun searchChassis(last5: String, userId: Long): SearchResult2 =
        runCatching {
            val resp = api.searchChassis(last5, userId)
            when {
                resp.isSuccessful -> SearchResult2.Success(resp.body()?.results ?: emptyList())
                resp.code() == 402 -> SearchResult2.SubscriptionExpired()
                else -> SearchResult2.Error("Search failed: ${resp.code()}")
            }
        }.getOrElse { SearchResult2.Error(it.message ?: "Network error") }
}
