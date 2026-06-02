package com.vkenterprises.vras.data.api

import com.vkenterprises.vras.data.models.*
import retrofit2.Response
import retrofit2.http.*

interface ApiService {

    @GET("api/mobile/agencies")
    suspend fun getAgencies(): Response<List<AgencyListItem>>

    // Full profile for the signed-in agency (name, address, every contact
    // number including extras). Slug is resolved server-side from the
    // X-Tenant-Token header added by the OkHttp interceptor.
    @GET("api/mobile/agency")
    suspend fun getAgencyInfo(): Response<AgencyInfo>

    // ── Mobile SMS OTP (MSG91) — phone-number verification before reg/login ──
    @POST("api/mobile/otp/send")
    suspend fun otpSend(@Body body: Map<String, String?>): Response<Map<String, Any>>

    @POST("api/mobile/otp/verify")
    suspend fun otpVerify(@Body body: Map<String, String?>): Response<Map<String, Any>>

    @POST("api/mobile/register")
    suspend fun register(@Body request: RegisterRequest): Response<Map<String, Any>>

    @POST("api/mobile/login")
    suspend fun login(@Body request: LoginRequest): Response<AuthResponse>

    // lite=true → skinny list (id, vehicle/chassis no, model, finance, branch,
    // date) so the search response is tiny and instant. Full detail is fetched
    // per-record via getRecord() only when a result is opened.
    @GET("api/mobile/search/rc/{last4}")
    suspend fun searchRc(
        @Path("last4")       last4: String,
        @Header("X-User-Id") userId: Long,
        @Query("lite")       lite: Boolean = true
    ): Response<SearchResponse>

    @GET("api/mobile/search/chassis/{last5}")
    suspend fun searchChassis(
        @Path("last5")       last5: String,
        @Header("X-User-Id") userId: Long,
        @Query("lite")       lite: Boolean = true
    ): Response<SearchResponse>

    // Full record detail for one search result (opened on tap).
    @GET("api/mobile/record/{id}")
    suspend fun getRecord(
        @Path("id")          id: Long,
        @Header("X-User-Id") userId: Long
    ): Response<SearchResult>

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

    @GET("api/mobile/me/status")
    suspend fun getMyStatus(@Header("X-User-Id") userId: Long): Response<UserStatusResponse>

    // ── KYC (Sandbox-backed; verification + storage happen server-side) ──────
    @POST("api/mobile/kyc/aadhaar/otp")
    suspend fun kycAadhaarOtp(@Body body: Map<String, String?>): Response<KycOtpResp>

    @POST("api/mobile/kyc/aadhaar/verify")
    suspend fun kycAadhaarVerify(@Header("X-User-Id") userId: Long, @Body body: Map<String, String?>): Response<KycAadhaarResp>

    // Registration flow: no user row exists yet, so no X-User-Id header. The
    // server verifies the OTP and returns the demographics WITHOUT storing —
    // the register call then persists them onto the new user.
    @POST("api/mobile/kyc/aadhaar/verify")
    suspend fun kycAadhaarVerifyAnon(@Body body: Map<String, String?>): Response<KycAadhaarResp>

    // KYC re-submission for a rejected agent (no token — slug + mobile in body).
    @POST("api/mobile/kyc/resubmit")
    suspend fun kycResubmit(@Body body: com.vkenterprises.vras.data.models.ResubmitKycRequest): Response<Map<String, Any>>

    @POST("api/mobile/kyc/pan")
    suspend fun kycPan(@Header("X-User-Id") userId: Long, @Body body: Map<String, String?>): Response<KycPanResp>

    @POST("api/mobile/kyc/bank")
    suspend fun kycBank(@Header("X-User-Id") userId: Long, @Body body: Map<String, String?>): Response<KycBankResp>

    @POST("api/mobile/heartbeat")
    suspend fun heartbeat(@Body request: HeartbeatRequest): Response<HeartbeatResponse>

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

    // ── Control Panel ──────────────────────────────────────────────────────
    @POST("api/mobile/admin/verify-admin-pass")
    suspend fun verifyAdminPass(
        @Header("X-User-Id") userId: Long,
        @Body request: VerifyAdminPassRequest
    ): Response<Map<String, Any>>

    @PATCH("api/mobile/admin/users/{userId}/active")
    suspend fun adminSetActive(
        @Header("X-User-Id") adminId: Long,
        @Path("userId") userId: Long,
        @Body request: SetUserFlagRequest
    ): Response<Map<String, Any>>

    @PATCH("api/mobile/admin/users/{userId}/stopped")
    suspend fun adminSetStopped(
        @Header("X-User-Id") adminId: Long,
        @Path("userId") userId: Long,
        @Body request: SetUserFlagRequest
    ): Response<Map<String, Any>>

    @PATCH("api/mobile/admin/users/{userId}/blacklisted")
    suspend fun adminSetBlacklisted(
        @Header("X-User-Id") adminId: Long,
        @Path("userId") userId: Long,
        @Body request: SetUserFlagRequest
    ): Response<Map<String, Any>>

    @PATCH("api/mobile/admin/users/{userId}/admin")
    suspend fun adminSetAdmin(
        @Header("X-User-Id") adminId: Long,
        @Path("userId") userId: Long,
        @Body request: SetUserFlagRequest
    ): Response<Map<String, Any>>
}
