package com.vkenterprises.vras.data.api

import com.vkenterprises.vras.data.models.*
import retrofit2.Response
import retrofit2.http.*

interface ApiService {

    @POST("api/mobile/register")
    suspend fun register(@Body request: RegisterRequest): Response<Map<String, Any>>

    @POST("api/mobile/login")
    suspend fun login(@Body request: LoginRequest): Response<AuthResponse>

    @GET("api/mobile/search/rc/{last4}")
    suspend fun searchRc(
        @Path("last4")       last4: String,
        @Header("X-User-Id") userId: Long
    ): Response<SearchResponse>

    @GET("api/mobile/search/chassis/{last5}")
    suspend fun searchChassis(
        @Path("last5")       last5: String,
        @Header("X-User-Id") userId: Long
    ): Response<SearchResponse>

    @GET("api/mobile/profile/{userId}")
    suspend fun getProfile(@Path("userId") userId: Long): Response<ProfileResponse>

    @PUT("api/mobile/profile/{userId}/pfp")
    suspend fun updatePfp(
        @Path("userId") userId: Long,
        @Body request: Map<String, String?>
    ): Response<Map<String, Any>>

    @GET("api/mobile/sync/branches")
    suspend fun getSyncBranches(): Response<SyncBranchResponse>

    @GET("api/mobile/sync/records/{branchId}")
    suspend fun getSyncRecords(
        @Path("branchId") branchId: Int,
        @Query("page")    page: Int = 0,
        @Query("size")    size: Int = 500
    ): Response<SyncRecordsResponse>

    @GET("api/mobile/stats")
    suspend fun getStats(): Response<StatsResponse>

    @POST("api/mobile/heartbeat")
    suspend fun heartbeat(@Body request: HeartbeatRequest): Response<Map<String, Any>>

    @POST("api/mobile/search-log")
    suspend fun logSearch(@Body request: SearchLogRequest): Response<Map<String, Any>>

    @GET("api/mobile/live-users")
    suspend fun getLiveUsers(
        @Header("X-User-Id") userId: Long
    ): Response<LiveUsersResponse>

    @POST("api/mobile/admin/verify-subs-pass")
    suspend fun verifySubsPass(
        @Header("X-User-Id") userId: Long,
        @Body request: VerifySubsPassRequest
    ): Response<Map<String, Any>>

    @GET("api/mobile/admin/users")
    suspend fun getAdminUsers(
        @Header("X-User-Id") userId: Long
    ): Response<List<AdminUserItem>>

    @GET("api/mobile/profile/{userId}/subscriptions")
    suspend fun getUserSubscriptions(
        @Header("X-User-Id") adminId: Long,
        @Path("userId") userId: Long
    ): Response<List<SubscriptionRecord>>

    @POST("api/mobile/admin/users/{userId}/subscriptions")
    suspend fun adminAddSubscription(
        @Header("X-User-Id") adminId: Long,
        @Path("userId") userId: Long,
        @Body request: AdminAddSubRequest
    ): Response<Map<String, Any>>

    @DELETE("api/mobile/admin/subscriptions/{subId}")
    suspend fun adminDeleteSubscription(
        @Header("X-User-Id") adminId: Long,
        @Path("subId") subId: Long
    ): Response<Map<String, Any>>
}
