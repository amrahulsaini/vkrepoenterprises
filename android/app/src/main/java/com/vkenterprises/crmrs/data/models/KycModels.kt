package com.vkenterprises.crmrs.data.models

data class KycOtpResp(
    val ok: Boolean = false,
    val referenceId: String? = null,
    val message: String? = null
)

data class KycAadhaarResp(
    val ok: Boolean = false,
    val verified: Boolean = false,
    val name: String? = null,
    val dob: String? = null,
    val gender: String? = null,
    val address: String? = null,
    val photo: String? = null,
    val message: String? = null
)

data class KycPanResp(
    val ok: Boolean = false,
    val verified: Boolean = false,
    val status: String? = null,
    val nameMatch: Boolean = false,
    val dobMatch: Boolean = false,
    val category: String? = null,
    val message: String? = null
)

data class KycBankResp(
    val ok: Boolean = false,
    val verified: Boolean = false,
    val nameAtBank: String? = null,
    val message: String? = null
)
