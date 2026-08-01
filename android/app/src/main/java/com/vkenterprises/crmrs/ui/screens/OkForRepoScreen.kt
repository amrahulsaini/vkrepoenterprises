package com.vkenterprises.crmrs.ui.screens

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
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
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.RepoSubmitRequest
import com.vkenterprises.crmrs.data.models.SearchResult
import com.vkenterprises.crmrs.utils.compressImageToBase64
import com.vkenterprises.crmrs.utils.createCameraImageUri
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.SearchViewModel
import coil.compose.AsyncImage
import kotlinx.coroutines.launch

@OptIn(
    ExperimentalMaterial3Api::class,
    androidx.compose.foundation.layout.ExperimentalLayoutApi::class,
    androidx.compose.ui.ExperimentalComposeUiApi::class
)
@Composable
fun OkForRepoScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val context    = LocalContext.current
    val scope      = rememberCoroutineScope()

    val imeVisible = WindowInsets.isImeVisible
    val keyboardController = LocalSoftwareKeyboardController.current
    val focusManager = LocalFocusManager.current
    BackHandler(enabled = imeVisible) {
        focusManager.clearFocus()
        keyboardController?.hide()
    }
    val ui         by searchVm.ui.collectAsState()
    val skinny     = ui.selectedResult
    val full       = ui.fullRecord
    val item: SearchResult? = full?.takeIf {
        skinny == null || it.vehicleNo == skinny.vehicleNo || it.chassisNo == skinny.chassisNo
    } ?: skinny
    val agentNameAuth  by authVm.userName.collectAsState(initial = "")
    val agentPhoneAuth by authVm.userMobile.collectAsState(initial = "")
    val userId         by authVm.userId.collectAsState(initial = -1L)

    var agencyName by remember { mutableStateOf(BuildConfig.AGENCY_NAME) }
    LaunchedEffect(Unit) {
        runCatching {
            val r = ApiClient.api.getAgencyInfo()
            if (r.isSuccessful) r.body()?.name?.takeIf { it.isNotBlank() }?.let { agencyName = it }
        }
    }

    var vehicleLocation   by remember { mutableStateOf("") }
    var agentName         by remember(item?.id) { mutableStateOf(agentNameAuth.uppercase()) }
    var parkingYardName   by remember { mutableStateOf("") }
    var parkingYardMobile by remember { mutableStateOf("") }
    var loadDetails       by remember { mutableStateOf("") }
    var addlNotes         by remember { mutableStateOf("") }
    var addlAmount        by remember { mutableStateOf("") }
    var confirmByName     by remember { mutableStateOf("") }
    var confirmByMobile   by remember { mutableStateOf("") }
    var executiveName     by remember(item?.id) { mutableStateOf(item?.executiveName.orEmpty().uppercase()) }
    var collectionUpdate  by remember { mutableStateOf("") }
    var remark            by remember { mutableStateOf("") }
    var cashAmount        by remember { mutableStateOf("") }

    var billingAction by remember { mutableStateOf("immediate") }
    var holdDays      by remember { mutableStateOf("") }
    var holdDate      by remember { mutableStateOf("") }
    var showHoldDatePicker by remember { mutableStateOf(false) }

    // Statuses already submitted for this vehicle — cannot be chosen again.
    var usedStatuses by remember(item?.id) { mutableStateOf<Set<String>>(emptySet()) }
    suspend fun reloadUsedStatuses() {
        val rec = item ?: return
        val used = runCatching {
            val r = ApiClient.api.getRepoStatuses(
                recordId  = rec.id.takeIf { it > 0 },
                vehicleNo = rec.vehicleNo.ifBlank { null },
                chassisNo = rec.chassisNo.ifBlank { null }
            )
            if (r.isSuccessful) r.body()?.statuses?.map { it.lowercase() }?.toSet() else null
        }.getOrNull() ?: emptySet()
        usedStatuses = used
        if (billingAction in used) {
            billingAction = listOf("immediate", "hold", "collection_done", "cancel")
                .firstOrNull { it !in used } ?: billingAction
        }
    }
    LaunchedEffect(item?.id, item?.vehicleNo, item?.chassisNo) { reloadUsedStatuses() }

    // Payment screenshot — mandatory for Collection done.
    var paymentUri by remember { mutableStateOf<Uri?>(null) }
    var paymentB64 by remember { mutableStateOf<String?>(null) }
    var showPaymentSource by remember { mutableStateOf(false) }
    var paymentCameraUri  by remember { mutableStateOf<Uri?>(null) }
    val paymentGallery = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) { paymentUri = uri; paymentB64 = runCatching { compressImageToBase64(context, uri) }.getOrNull() }
    }
    val paymentCamera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        if (ok) paymentCameraUri?.let { u -> paymentUri = u; paymentB64 = runCatching { compressImageToBase64(context, u) }.getOrNull() }
    }
    if (showPaymentSource) {
        ImageSourceDialog(
            title = "Attach payment screenshot",
            onCamera = { showPaymentSource = false; val u = createCameraImageUri(context); paymentCameraUri = u; paymentCamera.launch(u) },
            onGallery = { showPaymentSource = false; paymentGallery.launch("image/*") },
            onDismiss = { showPaymentSource = false }
        )
    }

    var submitting by remember { mutableStateOf(false) }
    var errorMsg   by remember { mutableStateOf<String?>(null) }
    var successMsg by remember { mutableStateOf<String?>(null) }

    fun buildMessage(): String = buildString {
        fun up(s: String?) = s?.trim().orEmpty().uppercase()
        appendLine("*Respected sir,*")
        appendLine("Loan No: *${up(item?.agreementNo).ifBlank { "-" }}*")
        appendLine("Customer Name: *${up(item?.customerName).ifBlank { "-" }}*")
        appendLine("Branch: *${up(item?.branchFromExcel).ifBlank { "null" }}*")
        appendLine("Vehicle No: *${up(item?.vehicleNo)}*")
        appendLine("Model/Maker: *${up(item?.model).ifBlank { "-" }}*")
        appendLine("Chassis No: *${up(item?.chassisNo)}*")
        appendLine("Engine No: *${up(item?.engineNo).ifBlank { "-" }}*")
        if (vehicleLocation.isNotBlank()) appendLine("Vehicle location: *${vehicleLocation.uppercase()}*")
        val statusLabel = when (billingAction) {
            "hold"            -> "HOLD FOR COLLECTION"
            "collection_done" -> "COLLECTION DONE"
            "cancel"          -> "CANCEL"
            else              -> "OK FOR BILLING"
        }
        appendLine("Status: *$statusLabel*")
        appendLine()
        val person = listOf(agentNameAuth.trim().uppercase(), agentPhoneAuth.trim())
            .filter { it.isNotBlank() }.joinToString(" - ")
        if (person.isNotBlank()) appendLine(person)
        appendLine("Agency Name: *${agencyName.uppercase()}*")

        fun f(label: String, v: String) { if (v.isNotBlank()) appendLine("$label: *${v.trim()}*") }
        fun comma(vararg vs: String) = vs.map { it.trim() }.filter { it.isNotBlank() }.joinToString(",")
        val extras = listOf(
            "Agent Name" to agentName,
            "Parking Yard Name" to parkingYardName,
            "Parking Yard Mobile" to parkingYardMobile,
            "Load Details" to loadDetails,
            "Additional Charges Notes,Amount" to comma(addlNotes, addlAmount),
            "Confirmation By (Name,Mobile)" to comma(confirmByName, confirmByMobile),
            "Executive Name" to executiveName,
            "Collection Update" to collectionUpdate,
            "Cash Received" to (if (billingAction == "collection_done") cashAmount.trim() else ""),
            "Remark" to remark
        ).filter { it.second.isNotBlank() }
        if (extras.isNotEmpty()) { appendLine(); extras.forEach { f(it.first, it.second) } }
    }

    fun sendWhatsApp() = openWa(context, buildMessage())

    fun submit() {
        if (submitting) return
        val rec = item ?: return
        val cashVal = cashAmount.trim().toDoubleOrNull() ?: 0.0
        if (billingAction == "collection_done" && cashVal <= 0.0 && paymentB64.isNullOrBlank()) {
            errorMsg = "For Collection done, enter the cash amount or attach the payment screenshot."
            return
        }
        submitting = true
        errorMsg = null
        successMsg = null
        scope.launch {
            val ok = runCatching {
                val resp = ApiClient.api.submitRepo(
                    userId = userId,
                    body = RepoSubmitRequest(
                        recordId          = rec.id,
                        loanNo            = rec.agreementNo,
                        customerName      = rec.customerName,
                        vehicleNo         = rec.vehicleNo,
                        model             = rec.model,
                        chassisNo         = rec.chassisNo,
                        engineNo          = rec.engineNo,
                        branch            = rec.branchFromExcel.ifBlank { rec.branchName },
                        agentName         = agentName.trim().uppercase().ifBlank { null },
                        parkingYardName   = parkingYardName.trim().uppercase().ifBlank { null },
                        parkingYardMobile = parkingYardMobile.trim().ifBlank { null },
                        loadDetails       = loadDetails.trim().uppercase().ifBlank { null },
                        addlChargesNotes  = addlNotes.trim().uppercase().ifBlank { null },
                        addlChargesAmount = addlAmount.trim().toDoubleOrNull(),
                        confirmationByName   = confirmByName.trim().uppercase().ifBlank { null },
                        confirmationByMobile = confirmByMobile.trim().ifBlank { null },
                        executiveName     = executiveName.trim().uppercase().ifBlank { null },
                        collectionUpdate  = collectionUpdate.trim().uppercase().ifBlank { null },
                        remark            = remark.trim().uppercase().ifBlank { null },
                        billingAction     = billingAction,
                        holdUntil         = holdDate.trim().ifBlank { null },
                        holdDays          = holdDays.trim().toIntOrNull(),
                        submittedByName   = agentNameAuth.trim().ifBlank { null },
                        paymentScreenshotB64 = paymentB64,
                        cashAmount        = cashVal.takeIf { it > 0.0 }
                    )
                )
                resp.isSuccessful
            }.getOrDefault(false)
            submitting = false
            if (ok) {
                sendWhatsApp()
                successMsg = "Saved & sent to WhatsApp."
                reloadUsedStatuses()
            } else {
                errorMsg = "Could not save. Check your connection and try again."
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("OK for Repo", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                }
            )
        }
    ) { pad ->
        if (item == null) {
            Box(Modifier.fillMaxSize().padding(pad), contentAlignment = Alignment.Center) {
                Text("No vehicle selected.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            return@Scaffold
        }

        Column(
            Modifier.padding(pad).fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Card(
                shape = RoundedCornerShape(12.dp),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    RepoSummaryRow("Vehicle No", item.vehicleNo)
                    RepoSummaryRow("Customer", item.customerName)
                    RepoSummaryRow("Chassis", item.chassisNo)
                    RepoSummaryRow("Loan No", item.agreementNo)
                }
            }

            Text("Billing decision", style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)

            BillingChoiceRow("OK for billing", "immediate", billingAction, "immediate" !in usedStatuses) { billingAction = it }
            BillingChoiceRow("Hold for collection", "hold", billingAction, "hold" !in usedStatuses) { billingAction = it }
            BillingChoiceRow("Collection done", "collection_done", billingAction, "collection_done" !in usedStatuses) { billingAction = it }
            BillingChoiceRow("Cancel", "cancel", billingAction, "cancel" !in usedStatuses) { billingAction = it }
            if (usedStatuses.isNotEmpty()) {
                Text(
                    "Some statuses are disabled — already submitted for this vehicle.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            if (billingAction == "hold") {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    OutlinedTextField(
                        value = holdDays, onValueChange = { holdDays = it.filter { c -> c.isDigit() } },
                        label = { Text("Hold days") },
                        keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true, modifier = Modifier.weight(1f), shape = RoundedCornerShape(10.dp)
                    )
                    Box(Modifier.weight(1f)) {
                        OutlinedTextField(
                            value = holdDate, onValueChange = {},
                            readOnly = true, enabled = false,
                            label = { Text("or pick a date") },
                            trailingIcon = { Icon(Icons.Default.CalendarMonth, null) },
                            singleLine = true, shape = RoundedCornerShape(10.dp),
                            colors = OutlinedTextFieldDefaults.colors(
                                disabledTextColor      = MaterialTheme.colorScheme.onSurface,
                                disabledLabelColor     = MaterialTheme.colorScheme.onSurfaceVariant,
                                disabledBorderColor    = MaterialTheme.colorScheme.outline,
                                disabledTrailingIconColor = MaterialTheme.colorScheme.onSurfaceVariant
                            ),
                            modifier = Modifier.fillMaxWidth()
                        )
                        Box(Modifier.matchParentSize().clickable { showHoldDatePicker = true })
                    }
                }
            }

            if (billingAction == "collection_done") {
                Text("How did the customer pay? *", style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)
                Text("Enter the cash amount, attach the online payment screenshot, or both.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)

                Field("Cash Amount Received", cashAmount, Icons.Default.Payments, KeyboardType.Number) {
                    cashAmount = it.filter { c -> c.isDigit() || c == '.' }
                }

                Text("Online payment screenshot", style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)
                if (paymentUri != null) {
                    AsyncImage(
                        model = paymentUri, contentDescription = "Payment screenshot",
                        modifier = Modifier.fillMaxWidth().height(200.dp)
                            .clip(RoundedCornerShape(10.dp)).clickable { showPaymentSource = true }
                    )
                    TextButton(onClick = { showPaymentSource = true }) { Text("Change screenshot") }
                } else {
                    OutlinedButton(
                        onClick = { showPaymentSource = true },
                        modifier = Modifier.fillMaxWidth().height(52.dp),
                        shape = RoundedCornerShape(10.dp)
                    ) {
                        Icon(Icons.Default.AttachFile, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(8.dp))
                        Text("Attach payment screenshot")
                    }
                }
            }

            HorizontalDivider(Modifier.padding(vertical = 4.dp))

            Text("Repo details", style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)
            Text("All fields are optional.", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)

            Field("Agent Name", agentName, Icons.Default.Person) { agentName = it }
            Field("Parking Yard Name", parkingYardName, Icons.Default.LocalParking) { parkingYardName = it }
            Field("Parking Yard Mobile", parkingYardMobile, Icons.Default.Call, KeyboardType.Phone) { parkingYardMobile = it }
            Field("Load Details", loadDetails, Icons.Default.LocalShipping) { loadDetails = it }
            Field("Additional Charges Notes", addlNotes, Icons.Default.Notes) { addlNotes = it }
            Field("Additional Charges Amount", addlAmount, Icons.Default.CurrencyRupee, KeyboardType.Number) { addlAmount = it }
            Field("Confirmation By (Name)", confirmByName, Icons.Default.HowToReg) { confirmByName = it }
            Field("Confirmation By (Mobile)", confirmByMobile, Icons.Default.Call, KeyboardType.Phone) { confirmByMobile = it }
            Field("Executive Name", executiveName, Icons.Default.Badge) { executiveName = it }
            Field("Collection Update", collectionUpdate, Icons.Default.Update) { collectionUpdate = it }
            Field("Remark", remark, Icons.Default.Comment) { remark = it }
            Field("Vehicle Location (for message)", vehicleLocation, Icons.Default.Place) { vehicleLocation = it }

            if (showHoldDatePicker) {
                val fmt = remember { java.time.format.DateTimeFormatter.ofPattern("yyyy-MM-dd") }
                val seed = remember(holdDate) {
                    runCatching {
                        if (holdDate.isNotBlank())
                            java.time.LocalDate.parse(holdDate, fmt)
                                .atStartOfDay(java.time.ZoneOffset.UTC).toInstant().toEpochMilli()
                        else null
                    }.getOrNull()
                }
                val dateState = rememberDatePickerState(
                    initialSelectedDateMillis = seed,
                    selectableDates = object : SelectableDates {
                        override fun isSelectableDate(utcTimeMillis: Long) =
                            utcTimeMillis >= java.time.LocalDate.now()
                                .atStartOfDay(java.time.ZoneOffset.UTC).toInstant().toEpochMilli()
                    }
                )
                DatePickerDialog(
                    onDismissRequest = { showHoldDatePicker = false },
                    confirmButton = {
                        TextButton(onClick = {
                            dateState.selectedDateMillis?.let { ms ->
                                holdDate = java.time.Instant.ofEpochMilli(ms)
                                    .atZone(java.time.ZoneOffset.UTC).toLocalDate().format(fmt)
                                holdDays = ""
                            }
                            showHoldDatePicker = false
                        }) { Text("OK") }
                    },
                    dismissButton = {
                        TextButton(onClick = { showHoldDatePicker = false }) { Text("Cancel") }
                    }
                ) { DatePicker(state = dateState) }
            }

            errorMsg?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            }
            successMsg?.let {
                Text(it, color = Color(0xFF2E7D32), style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.Bold)
            }

            Button(
                onClick = { submit() },
                enabled = !submitting,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(10.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF2E7D32), contentColor = Color.White)
            ) {
                if (submitting) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp, color = Color.White)
                else {
                    Icon(Icons.Default.Send, null, Modifier.size(18.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("Submit & Send WhatsApp", fontWeight = FontWeight.Bold)
                }
            }
            Spacer(Modifier.height(16.dp))
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun Field(
    label: String,
    value: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    keyboard: KeyboardType = KeyboardType.Text,
    onChange: (String) -> Unit
) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        label = { Text(label) },
        leadingIcon = { Icon(icon, null) },
        // Caps via the keyboard's own mode (not a text transform) so word
        // suggestions/predictions keep working. Number fields stay as-is.
        keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
            keyboardType = keyboard,
            capitalization = if (keyboard == KeyboardType.Text)
                androidx.compose.ui.text.input.KeyboardCapitalization.Characters
            else androidx.compose.ui.text.input.KeyboardCapitalization.None
        ),
        // Always render uppercase on screen (display only — the raw value keeps
        // its composing region, so keyboard suggestions still show).
        visualTransformation = if (keyboard == KeyboardType.Text)
            androidx.compose.ui.text.input.VisualTransformation { t ->
                androidx.compose.ui.text.input.TransformedText(
                    androidx.compose.ui.text.AnnotatedString(t.text.uppercase()),
                    androidx.compose.ui.text.input.OffsetMapping.Identity
                )
            }
        else androidx.compose.ui.text.input.VisualTransformation.None,
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(10.dp)
    )
}

@Composable
private fun BillingChoiceRow(label: String, value: String, selected: String, enabled: Boolean = true, onSelect: (String) -> Unit) {
    Row(
        Modifier.fillMaxWidth().then(if (enabled) Modifier else Modifier.alpha(0.4f)),
        verticalAlignment = Alignment.CenterVertically
    ) {
        RadioButton(selected = selected == value, enabled = enabled, onClick = { if (enabled) onSelect(value) })
        Text(label, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium)
    }
}

@Composable
private fun RepoSummaryRow(label: String, value: String?) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value.orEmpty().ifBlank { "—" }, style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Bold, fontFamily = FontFamily.Monospace)
    }
}

private fun openWa(context: Context, message: String) {
    val base = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_TEXT, message)
    }
    val pm = context.packageManager
    val target = listOf("com.whatsapp", "com.whatsapp.w4b").firstOrNull { p ->
        runCatching { pm.getPackageInfo(p, 0); true }.getOrDefault(false)
    }
    val launch = if (target != null) Intent(base).setPackage(target)
                 else Intent.createChooser(base, "Share via")
    runCatching { context.startActivity(launch) }.onFailure {
        runCatching { context.startActivity(Intent.createChooser(base, "Share via")) }
    }
}
