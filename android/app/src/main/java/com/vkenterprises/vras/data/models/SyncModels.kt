package com.vkenterprises.vras.data.models

data class SyncBranch(
    val branchId: Int,
    val branchName: String,
    val financerName: String,
    val totalRecords: Long,
    val uploadedAt: String?
)

data class SyncBranchResponse(
    val success: Boolean,
    val branchCount: Int,
    val totalRecords: Long,
    val branches: List<SyncBranch>
)

data class SyncRecord(
    val id: Long,
    val vehicleNo: String,
    val chassisNo: String,
    val engineNo: String,
    val model: String,
    val customerName: String,
    val last4: String,
    val last5: String
)

data class SyncRecordsResponse(
    val success: Boolean,
    val branchId: Int,
    val page: Int,
    val pageSize: Int,
    val hasMore: Boolean,
    val records: List<SyncRecord>
)

data class StatsResponse(
    val success: Boolean,
    val vehicleRecords: Long,
    val rcRecords: Long,
    val chassisRecords: Long
)
