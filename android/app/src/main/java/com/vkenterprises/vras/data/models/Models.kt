package com.vkenterprises.vras.data.models

import com.google.gson.annotations.SerializedName

data class RegisterRequest(
    val mobile: String,
    val name: String,
    val address: String?,
    val pincode: String?,
    val pfpBase64: String?,
    val deviceId: String,
    // KYC
    val aadhaarFront: String?,
    val aadhaarBack: String?,
    val panFront: String?,
    val accountNumber: String?,
    val ifscCode: String?
)

data class LoginRequest(
    val mobile: String,
    val deviceId: String
)

data class AuthResponse(
    val success: Boolean,
    val message: String,
    val reason: String,
    val userId: Long?,
    val name: String?,
    val mobile: String?,
    val isAdmin: Boolean,
    val pfpBase64: String?,
    val subscriptionEndDate: String?
)

data class SearchResult(
    val id: Long,
    val vehicleNo: String,
    val chassisNo: String,
    val engineNo: String,
    val model: String,
    val agreementNo: String,
    val customerName: String,
    val customerContact: String,
    val customerAddress: String,
    val financer: String,
    val branchName: String,
    val firstContact: String,
    val secondContact: String,
    val thirdContact: String,
    val address: String,
    val region: String,
    val area: String,
    val bucket: String,
    @SerializedName("gV") val gv: String,
    @SerializedName("oD") val od: String,
    val seasoning: String,
    val tbrFlag: String,
    val sec9: String,
    val sec17: String,
    val level1: String,
    val level1Contact: String,
    val level2: String,
    val level2Contact: String,
    val level3: String,
    val level3Contact: String,
    val level4: String,
    val level4Contact: String,
    val senderMail1: String,
    val senderMail2: String,
    val executiveName: String,
    val pos: String,
    val toss: String,
    val remark: String,
    val branchFromExcel: String,
    val createdOn: String
)

data class SearchResponse(
    val success: Boolean,
    val mode: String,
    val query: String,
    val count: Int,
    val results: List<SearchResult>
)

data class ApiError(
    val success: Boolean,
    val message: String
)

// KYC + Profile models
data class KycInfo(
    val kycSubmitted: Boolean,
    val aadhaarFront: String?,
    val aadhaarBack: String?,
    val panFront: String?
)

data class SubscriptionRecord(
    val id: Long,
    val startDate: String,
    val endDate: String,
    val amount: Double,
    val notes: String?,
    val isActive: Boolean
)

data class ProfileResponse(
    val userId: Long,
    val name: String,
    val mobile: String,
    val address: String?,
    val pincode: String?,
    val pfpBase64: String?,
    val isActive: Boolean,
    val isAdmin: Boolean,
    val balance: Double,
    val createdAt: String,
    val accountNumber: String?,
    val ifscCode: String?,
    val kyc: KycInfo,
    val subscriptions: List<SubscriptionRecord>
)

data class HeartbeatRequest(
    val userId: Long,
    val lat: Double?,
    val lng: Double?
)

data class HeartbeatResponse(
    val success: Boolean,
    val isStopped: Boolean,
    val isBlacklisted: Boolean
)

data class LiveUser(
    val id: Long,
    val name: String,
    val mobile: String,
    val lastSeen: String,
    val lat: Double?,
    val lng: Double?
)

data class LiveUsersResponse(
    val success: Boolean,
    val users: List<LiveUser>
)

data class SearchLogRequest(
    val userId: Long,
    val vehicleNo: String,
    val chassisNo: String,
    val model: String,
    val lat: Double?,
    val lng: Double?,
    val address: String?,
    val deviceTimeIso: String
)

// Local session stored in DataStore
data class SessionUser(
    val userId: Long,
    val name: String,
    val mobile: String,
    val isAdmin: Boolean,
    val subscriptionEndDate: String?
)

// Admin subs management
data class VerifySubsPassRequest(val password: String)
data class AdminUserItem(
    val id: Long,
    val name: String,
    val mobile: String,
    val address: String?,
    val subEnd: String?
)
data class AdminAddSubRequest(
    val startDate: String,
    val endDate: String,
    val amount: Double,
    val notes: String?
)
