package com.vkenterprises.crmrs.ui.screens

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.R
import com.vkenterprises.crmrs.data.models.BillingSettings
import com.vkenterprises.crmrs.utils.BillingDoc
import com.vkenterprises.crmrs.utils.RepoPdf
import com.vkenterprises.crmrs.utils.compressImageToBase64
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.RepoViewModel
import coil.compose.AsyncImage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

private const val DOCX_MIME =
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document"

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BillingPreviewScreen(
    repoVm: RepoViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui by repoVm.ui.collectAsState()
    val userId by authVm.userId.collectAsState(initial = -1L)
    val agencyNameDefault by authVm.agencyName.collectAsState(initial = null)
    val record = ui.selectedRecord
    val billing = ui.billingSettings
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) { if (ui.billingSettings == null && userId > 0) repoVm.loadBillingSettings(userId) }

    val today = remember { SimpleDateFormat("dd/MM/yyyy", Locale.ENGLISH).format(Date()) }
    val todayDash = remember { SimpleDateFormat("dd-MM-yyyy", Locale.ENGLISH).format(Date()) }
    val headOffice = ui.selectedHeadOffice?.name?.uppercase().orEmpty()

    var toFinance by remember(headOffice) { mutableStateOf(headOffice) }
    var invoiceDate by remember { mutableStateOf(today) }
    var invoiceNo by remember { mutableStateOf("") }
    var branch by remember(record) {
        mutableStateOf(record?.let { it.branchName.ifBlank { it.branchFromExcel } } ?: "")
    }
    var confirmationBy by remember { mutableStateOf("") }

    var agriLoanNo by remember(record) { mutableStateOf(record?.agreementNo ?: "") }
    var customerName by remember(record) { mutableStateOf(record?.customerName ?: "") }
    var makeModel by remember(record) { mutableStateOf(record?.model ?: "") }
    var rcNo by remember(record) { mutableStateOf(record?.vehicleNo ?: "") }

    var dateOfRepossession by remember { mutableStateOf(todayDash) }
    var enclosed by remember { mutableStateOf("REPO KIT") }
    var qty by remember { mutableStateOf("01") }
    var repoChargesWords by remember { mutableStateOf("") }
    var repoChargesAmount by remember { mutableStateOf("") }
    var additionalCharges by remember { mutableStateOf("NA") }
    var totalGrossWords by remember { mutableStateOf("") }
    var totalGrossAmount by remember { mutableStateOf("") }

    var agencyName by remember(billing, agencyNameDefault) {
        mutableStateOf(billing?.agencyName ?: agencyNameDefault ?: BuildConfig.AGENCY_NAME)
    }
    var headerAddress by remember(billing) { mutableStateOf(billing?.headerAddress ?: BuildConfig.AGENCY_ADDRESS) }
    var headerContact by remember(billing) { mutableStateOf(billing?.headerContact ?: BuildConfig.AGENCY_MOBILE) }
    var headerEmail by remember(billing) { mutableStateOf(billing?.headerEmail ?: "") }
    var parkingYard by remember(billing) { mutableStateOf(billing?.parkingYard ?: "") }
    var panNo by remember(billing) { mutableStateOf(billing?.panNo ?: "") }
    var gstState by remember(billing) { mutableStateOf(billing?.gstState ?: "") }
    var bankAccountName by remember(billing) { mutableStateOf(billing?.bankAccountName ?: "") }
    var accountNo by remember(billing) { mutableStateOf(billing?.accountNo ?: "") }
    var ifscCode by remember(billing) { mutableStateOf(billing?.ifscCode ?: "") }
    var bankBranch by remember(billing) { mutableStateOf(billing?.bankBranch ?: "") }
    var paymentName by remember(billing, agencyName) { mutableStateOf(billing?.paymentName ?: agencyName) }
    var footerLine by remember(billing) { mutableStateOf(billing?.footerLine ?: "") }

    var generating by remember { mutableStateOf(false) }
    var asDocx by remember { mutableStateOf(false) }

    val logoPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) scope.launch {
            val b64 = withContext(Dispatchers.IO) { compressImageToBase64(context, uri) }
            if (b64 != null) repoVm.uploadBillingLogo(userId, b64)
        }
    }

    fun doGenerate() {
        generating = true
        scope.launch {
            repoVm.saveBillingSettings(userId, BillingSettings(
                agencyName = agencyName.trim(),
                headerAddress = headerAddress.trim(),
                headerContact = headerContact.trim(),
                headerEmail = headerEmail.trim(),
                panNo = panNo.trim(),
                gstState = gstState.trim(),
                bankAccountName = bankAccountName.trim(),
                accountNo = accountNo.trim(),
                ifscCode = ifscCode.trim(),
                bankBranch = bankBranch.trim(),
                parkingYard = parkingYard.trim(),
                paymentName = paymentName.trim(),
                footerLine = footerLine.trim()
            ))
            val logoUrl = repoVm.ui.value.billingSettings?.logoUrl
            val result = withContext(Dispatchers.IO) {
                val logo = if (!logoUrl.isNullOrBlank()) RepoPdf.loadBitmap(logoUrl)
                           else BillingDoc.drawableToBitmap(context, R.drawable.agency_logo)
                val data = BillingDoc.BillingData(
                    logo = logo,
                    agencyName = agencyName.trim(),
                    headerAddress = headerAddress.trim(),
                    headerContact = headerContact.trim(),
                    headerEmail = headerEmail.trim(),
                    toFinance = toFinance.trim(),
                    invoiceDate = invoiceDate.trim(),
                    invoiceNo = invoiceNo.trim(),
                    branch = branch.trim(),
                    confirmationBy = confirmationBy.trim(),
                    agriLoanNo = agriLoanNo.trim(),
                    customerName = customerName.trim(),
                    makeModel = makeModel.trim(),
                    rcNo = rcNo.trim(),
                    dateOfRepossession = dateOfRepossession.trim(),
                    parkingYard = parkingYard.trim(),
                    agencyNameRow = agencyName.trim(),
                    enclosed = enclosed.trim(),
                    qty = qty.trim(),
                    repoChargesWords = repoChargesWords.trim(),
                    repoChargesAmount = repoChargesAmount.trim(),
                    additionalCharges = additionalCharges.trim(),
                    panNo = panNo.trim(),
                    gstState = gstState.trim(),
                    bankAccountName = bankAccountName.trim(),
                    accountNo = accountNo.trim(),
                    ifscCode = ifscCode.trim(),
                    bankBranch = bankBranch.trim(),
                    totalGrossWords = totalGrossWords.trim(),
                    totalGrossAmount = totalGrossAmount.trim(),
                    paymentName = paymentName.trim(),
                    footerLine = footerLine.trim()
                )
                if (asDocx) BillingDoc.generateDocx(context, data) to DOCX_MIME
                else BillingDoc.generatePdf(context, data) to "application/pdf"
            }
            generating = false
            RepoPdf.open(context, result.first, result.second)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Repossession Bill", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                }
            )
        },
        bottomBar = {
            Surface(shadowElevation = 8.dp) {
                Column(Modifier.navigationBarsPadding().padding(horizontal = 12.dp, vertical = 10.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Format", style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold)
                        FilterChip(selected = !asDocx, onClick = { asDocx = false }, label = { Text("PDF") })
                        FilterChip(selected = asDocx, onClick = { asDocx = true }, label = { Text("DOCX") })
                    }
                    Button(onClick = { doGenerate() }, enabled = !generating,
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier.fillMaxWidth().height(48.dp)) {
                        if (generating) CircularProgressIndicator(Modifier.size(20.dp),
                            color = MaterialTheme.colorScheme.onPrimary, strokeWidth = 2.dp)
                        else { Icon(Icons.Default.ReceiptLong, null); Spacer(Modifier.width(8.dp))
                            Text("Generate Bill", fontWeight = FontWeight.Bold) }
                    }
                }
            }
        }
    ) { pad ->
        Column(
            Modifier.padding(pad).fillMaxSize().verticalScroll(rememberScrollState())
                .padding(horizontal = 14.dp, vertical = 10.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            BillSection("Letterhead Logo")
            BillLogoRow(
                logoUrl = ui.billingSettings?.logoUrl,
                uploading = ui.uploadingLogo,
                onPick = { logoPicker.launch("image/*") }
            )

            BillSection("Invoice")
            BillField("To (Head Office)", toFinance) { toFinance = it }
            BillField("Invoice Date", invoiceDate) { invoiceDate = it }
            BillField("Invoice No", invoiceNo) { invoiceNo = it }
            BillField("Branch", branch) { branch = it }
            BillField("Confirmation By", confirmationBy) { confirmationBy = it }

            BillSection("Vehicle (auto-filled)")
            BillField("Agri-Loan No", agriLoanNo) { agriLoanNo = it }
            BillField("Name of Customer", customerName) { customerName = it }
            BillField("Make-Model", makeModel) { makeModel = it }
            BillField("RC No", rcNo) { rcNo = it }

            BillSection("Repossession")
            BillField("Date of Repossession", dateOfRepossession) { dateOfRepossession = it }
            BillField("Parking Yard Name", parkingYard) { parkingYard = it }
            BillField("Enclosed", enclosed) { enclosed = it }
            BillField("Qty", qty) { qty = it }
            BillField("Repo Charges (in words)", repoChargesWords, "e.g. NINE THOUSAND ONLY") { repoChargesWords = it }
            BillField("Repo Charges (amount)", repoChargesAmount, "e.g. RS.9000/-") { repoChargesAmount = it }
            BillField("Additional Charges", additionalCharges) { additionalCharges = it }
            BillField("Total Gross (in words)", totalGrossWords) { totalGrossWords = it }
            BillField("Total Gross (amount)", totalGrossAmount) { totalGrossAmount = it }

            BillSection("Agency & Bank (saved)")
            BillField("Agency Name", agencyName) { agencyName = it }
            BillField("Header Address", headerAddress, singleLine = false) { headerAddress = it }
            BillField("Header Contact", headerContact) { headerContact = it }
            BillField("Header Email", headerEmail) { headerEmail = it }
            BillField("PAN No", panNo) { panNo = it }
            BillField("GST State", gstState) { gstState = it }
            BillField("Bank Account Name", bankAccountName) { bankAccountName = it }
            BillField("Account No", accountNo) { accountNo = it }
            BillField("IFSC Code", ifscCode) { ifscCode = it }
            BillField("Bank Branch", bankBranch) { bankBranch = it }
            BillField("Pay In The Name Of", paymentName) { paymentName = it }
            BillField("Footer Line", footerLine, "e.g. Pune 411027") { footerLine = it }

            Spacer(Modifier.height(80.dp))
        }
    }
}

@Composable
private fun BillLogoRow(logoUrl: String?, uploading: Boolean, onPick: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        Surface(
            shape = RoundedCornerShape(10.dp),
            color = MaterialTheme.colorScheme.surfaceVariant,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
            modifier = Modifier.size(72.dp)
        ) {
            Box(contentAlignment = Alignment.Center) {
                when {
                    uploading -> CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.dp)
                    !logoUrl.isNullOrBlank() -> AsyncImage(
                        model = logoUrl, contentDescription = "Letterhead logo",
                        modifier = Modifier.fillMaxSize().padding(6.dp))
                    else -> Icon(Icons.Default.Image, null, tint = MaterialTheme.colorScheme.outline)
                }
            }
        }
        Column(Modifier.weight(1f)) {
            Text(
                if (logoUrl.isNullOrBlank()) "Using the app's agency logo. Upload a custom letterhead logo."
                else "Custom letterhead logo set — appears on top of the bill.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(6.dp))
            OutlinedButton(onClick = onPick, enabled = !uploading, shape = RoundedCornerShape(8.dp)) {
                Icon(Icons.Default.Upload, null, Modifier.size(16.dp)); Spacer(Modifier.width(6.dp))
                Text(if (logoUrl.isNullOrBlank()) "Upload Logo" else "Change Logo")
            }
        }
    }
}

@Composable
private fun BillSection(text: String) {
    Text(text.uppercase(), style = MaterialTheme.typography.labelSmall,
        fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 6.dp))
}

@Composable
private fun BillField(
    label: String,
    value: String,
    placeholder: String = "",
    singleLine: Boolean = true,
    onChange: (String) -> Unit
) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        label = { Text(label) },
        placeholder = { if (placeholder.isNotBlank()) Text(placeholder, style = MaterialTheme.typography.bodySmall) },
        singleLine = singleLine,
        minLines = if (singleLine) 1 else 2,
        shape = RoundedCornerShape(10.dp),
        modifier = Modifier.fillMaxWidth()
    )
}
