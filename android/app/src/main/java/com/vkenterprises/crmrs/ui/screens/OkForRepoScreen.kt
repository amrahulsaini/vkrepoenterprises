package com.vkenterprises.crmrs.ui.screens

import android.content.Context
import android.content.Intent
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
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
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.SearchViewModel
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OkForRepoScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val context    = LocalContext.current
    val scope      = rememberCoroutineScope()
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

    var billingAction by remember { mutableStateOf("immediate") }
    var holdDays      by remember { mutableStateOf("") }
    var holdDate      by remember { mutableStateOf("") }
    var showHoldDatePicker by remember { mutableStateOf(false) }

    var submitting by remember { mutableStateOf(false) }
    var errorMsg   by remember { mutableStateOf<String?>(null) }

    fun buildMessage(): String = buildString {
        fun up(s: String?) = s?.trim().orEmpty().uppercase()
        appendLine("*Respected sir,*")
        appendLine("Loan No: *${up(item?.agreementNo).ifBlank { "-" }}*")
        appendLine("Customer Name: *${up(item?.customerName).ifBlank { "-" }}*")
        appendLine("Branch: *${up(item?.branchFromExcel).ifBlank { up(item?.branchName).ifBlank { "-" } }}*")
        appendLine("Vehicle No: *${up(item?.vehicleNo)}*")
        appendLine("Model/Maker: *${up(item?.model).ifBlank { "-" }}*")
        appendLine("Chassis No: *${up(item?.chassisNo)}*")
        appendLine("Engine No: *${up(item?.engineNo).ifBlank { "-" }}*")
        if (vehicleLocation.isNotBlank()) appendLine("Vehicle location: *${vehicleLocation.uppercase()}*")
        appendLine("Status: *Ok for repo.*")
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
            "Remark" to remark
        ).filter { it.second.isNotBlank() }
        if (extras.isNotEmpty()) { appendLine(); extras.forEach { f(it.first, it.second) } }
    }

    fun sendWhatsApp() = openWa(context, buildMessage())

    fun submit() {
        if (submitting) return
        val rec = item ?: return
        submitting = true
        errorMsg = null
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
                        agentName         = agentName.trim().ifBlank { null },
                        parkingYardName   = parkingYardName.trim().ifBlank { null },
                        parkingYardMobile = parkingYardMobile.trim().ifBlank { null },
                        loadDetails       = loadDetails.trim().ifBlank { null },
                        addlChargesNotes  = addlNotes.trim().ifBlank { null },
                        addlChargesAmount = addlAmount.trim().toDoubleOrNull(),
                        confirmationByName   = confirmByName.trim().ifBlank { null },
                        confirmationByMobile = confirmByMobile.trim().ifBlank { null },
                        executiveName     = executiveName.trim().ifBlank { null },
                        collectionUpdate  = collectionUpdate.trim().ifBlank { null },
                        remark            = remark.trim().ifBlank { null },
                        billingAction     = billingAction,
                        holdUntil         = holdDate.trim().ifBlank { null },
                        holdDays          = holdDays.trim().toIntOrNull(),
                        submittedByName   = agentNameAuth.trim().ifBlank { null }
                    )
                )
                resp.isSuccessful
            }.getOrDefault(false)
            submitting = false
            if (ok) {
                sendWhatsApp()
                nav.popBackStack()
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

            HorizontalDivider(Modifier.padding(vertical = 4.dp))

            Text("Billing decision", style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)

            BillingChoiceRow("OK for billing", "immediate", billingAction) { billingAction = it }
            BillingChoiceRow("Hold for collection", "hold", billingAction) { billingAction = it }
            BillingChoiceRow("Cancel", "cancel", billingAction) { billingAction = it }

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
        onValueChange = { onChange(it.uppercase()) },
        label = { Text(label) },
        leadingIcon = { Icon(icon, null) },
        keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = keyboard),
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(10.dp)
    )
}

@Composable
private fun BillingChoiceRow(label: String, value: String, selected: String, onSelect: (String) -> Unit) {
    Row(
        Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically
    ) {
        RadioButton(selected = selected == value, onClick = { onSelect(value) })
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
