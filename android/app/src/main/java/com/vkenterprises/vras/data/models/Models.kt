package com.vkenterprises.vras.data.models

import com.google.gson.annotations.SerializedName

data class RegisterRequest(
    val mobile: String,
    val name: String,
    val address: String?,
    val pincode: String?,
    val pfpBase64: String?,
    val deviceId: String
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
    val releaseStatus: String,
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

// Local session stored in DataStore
data class SessionUser(
    val userId: Long,
    val name: String,
    val mobile: String,
    val isAdmin: Boolean,
    val subscriptionEndDate: String?
)
