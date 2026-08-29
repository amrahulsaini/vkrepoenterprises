package com.vkenterprises.crmrs.data.models

data class SyncBranch(
    val branchId: Int,
    val branchName: String,
    val financerName: String,
    val totalRecords: Long,
    val uploadedAt: String?,
    val contact1: String = "",
    val contact2: String = "",
    val contact3: String = "",
    val address: String = ""
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
    val last5: String,
    val agreementNo: String = "",
    val customerContact: String = "",
    val customerAddress: String = "",
    val region: String = "",
    val area: String = "",
    val bucket: String = "",
    val gv: String = "",
    val od: String = "",
    val seasoning: String = "",
    val tbrFlag: String = "",
    val sec9: String = "",
    val sec17: String = "",
    val level1: String = "",
    val level1Contact: String = "",
    val level2: String = "",
    val level2Contact: String = "",
    val level3: String = "",
    val level3Contact: String = "",
    val level4: String = "",
    val level4Contact: String = "",
    val senderMail1: String = "",
    val senderMail2: String = "",
    val executiveName: String = "",
    val pos: String = "",
    val toss: String = "",
    val remark: String = "",
    val branchFromExcel: String = "",
    val createdOn: String = ""
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
