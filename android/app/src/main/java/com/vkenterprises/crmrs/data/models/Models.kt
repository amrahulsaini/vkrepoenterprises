package com.vkenterprises.crmrs.data.models

import com.google.gson.annotations.SerializedName

data class RegisterRequest(
    val mobile: String,
    val name: String,
    val address: String?,
    val pincode: String?,
    val pfpBase64: String?,
    val deviceId: String,
    val aadhaarFront: String?,
    val aadhaarBack: String?,
    val panFront: String?,
    val accountNumber: String?,
    val ifscCode: String?,
    val slug: String? = null,
    val agencyMobile: String? = null,
    val selfieWithAadhaar: String? = null,
    val aadhaarNumber: String? = null,
    val aadhaarName: String? = null,
    val aadhaarDob: String? = null,
    val aadhaarGender: String? = null,
    val aadhaarAddress: String? = null,
    val aadhaarVerified: Boolean = false,
    val regLat: Double? = null,
    val regLng: Double? = null,
    val regLocation: String? = null,
    val aadhaarPhoto: String? = null
)

data class LoginRequest(
    val mobile: String,
    val deviceId: String,
    val slug: String? = null
)

data class ResubmitKycRequest(
    val slug: String,
    val mobile: String,
    val aadhaarFront: String?,
    val aadhaarBack: String?,
    val panFront: String?,
    val selfieWithAadhaar: String?,
    val aadhaarPhoto: String?,
    val aadhaarNumber: String?,
    val aadhaarName: String?,
    val aadhaarDob: String?,
    val aadhaarGender: String?,
    val aadhaarAddress: String?,
    val aadhaarVerified: Boolean = false,
    val regLat: Double? = null,
    val regLng: Double? = null,
    val regLocation: String? = null
)

data class AuthResponse(
    val success: Boolean,
    val message: String,
    val reason: String,
    val userId: Long?,
    val name: String?,
    val mobile: String?,
    val isAdmin: Boolean,
    val pfpUrl: String?,
    val subscriptionEndDate: String?,
    val tenantToken: String? = null
)

data class AgencyListItem(
    val id: Long,
    val name: String,
    val slug: String,
    val logoPath: String = ""
)

data class AgencyInfo(
    val name: String = "",
    val address: String = "",
    val mobiles: List<String> = emptyList(),
    val logoPath: String = ""
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

data class HeadOffice(
    val id: Long,
    val name: String,
    val totalRecords: Long = 0
)

data class RepoLetterSettings(
    val financeId: Long = 0,
    val agencyName: String? = null,
    val authorizedBy: String? = null,
    val policeStation: String? = null,
    val policeAddress: String? = null
)

data class SaveRepoSettingsRequest(
    val agencyName: String?,
    val authorizedBy: String?,
    val policeStation: String?,
    val policeAddress: String?
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

data class KycInfo(
    val kycSubmitted: Boolean,
    val aadhaarFront: String?,
    val aadhaarBack: String?,
    val panFront: String?,
    val selfie: String? = null,
    val aadhaarPhoto: String? = null,
    val kycStatus: String? = null,
    val rejectNote: String? = null,
    val aadhaarVerified: Boolean = false,
    val aadhaarNumber: String? = null,
    val aadhaarLast4: String? = null,
    val aadhaarName: String? = null,
    val aadhaarDob: String? = null,
    val aadhaarGender: String? = null,
    val aadhaarAddress: String? = null,
    val lat: Double? = null,
    val lng: Double? = null,
    val locationLabel: String? = null
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
    val pfpUrl: String?,
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

data class UserStatusResponse(
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

data class SessionUser(
    val userId: Long,
    val name: String,
    val mobile: String,
    val isAdmin: Boolean,
    val subscriptionEndDate: String?
)

data class VerifySubsPassRequest(val password: String)
data class AdminUserItem(
    val id: Long,
    val name: String,
    val mobile: String,
    val address: String?,
    val subEnd: String?,
    val isActive: Boolean = false,
    val isAdmin: Boolean = false,
    val isStopped: Boolean = false,
    val isBlacklisted: Boolean = false
)
data class AdminAddSubRequest(
    val startDate: String,
    val endDate: String,
    val amount: Double,
    val notes: String?
)

data class VerifyAdminPassRequest(val password: String)
data class SetUserFlagRequest(val value: Boolean)
data class SetKycStatusRequest(val status: String, val note: String?)
