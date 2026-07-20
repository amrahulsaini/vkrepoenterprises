package com.vkenterprises.crmrs.ui.screens

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
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
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.vkenterprises.crmrs.data.models.RepoTaskItem
import com.vkenterprises.crmrs.viewmodel.TaskManagerUiState
import com.vkenterprises.crmrs.viewmodel.TaskManagerViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TaskManagerScreen(
    vm: TaskManagerViewModel,
    userId: Long,
    nav: NavController
) {
    val ui by vm.ui.collectAsState()

    LaunchedEffect(userId) { if (userId > 0) vm.init(userId) }

    BackHandler(enabled = ui.editing != null) { vm.cancelEdit() }

    ui.editing?.let { item ->
        TaskEditSheet(
            item    = item,
            saving  = ui.saving,
            onCancel = { vm.cancelEdit() },
            onSave   = { vm.saveEdit(it) }
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Task Manager", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = {
                        if (ui.editing != null) vm.cancelEdit() else nav.popBackStack()
                    }) { Icon(Icons.Default.ArrowBack, null) }
                },
                actions = {
                    IconButton(onClick = { vm.load() }) { Icon(Icons.Default.Refresh, null) }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface)
            )
        }
    ) { pad ->
        Box(Modifier.padding(pad).fillMaxSize()) {
            Column(Modifier.fillMaxSize()) {
                MonthHeader(ui, onPrev = { vm.prevMonth() }, onNext = { vm.nextMonth() })
                ProgressCard(ui)

                if (ui.loading) {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator()
                    }
                } else if (ui.items.isEmpty()) {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text(
                            "No OK-for-Repo entries in ${ui.monthName} ${ui.year}.",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                } else {
                    LazyColumn(
                        Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(12.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        items(ui.items, key = { it.id }) { item ->
                            TaskRow(item) { vm.startEdit(item) }
                        }
                    }
                }
            }

            val msg = ui.errorMsg ?: ui.savedMsg
            msg?.let {
                Snackbar(
                    modifier = Modifier.align(Alignment.BottomCenter).padding(12.dp),
                    action = { TextButton(onClick = { vm.dismissMessages() }) { Text("OK") } }
                ) { Text(it) }
            }
        }
    }
}

@Composable
private fun MonthHeader(ui: TaskManagerUiState, onPrev: () -> Unit, onNext: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        IconButton(onClick = onPrev) { Icon(Icons.Default.ChevronLeft, "Previous month") }
        Text(
            "${ui.monthName} ${ui.year}",
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold
        )
        IconButton(onClick = onNext) { Icon(Icons.Default.ChevronRight, "Next month") }
    }
}

@Composable
private fun ProgressCard(ui: TaskManagerUiState) {
    val accent = when {
        ui.targetMet -> Color(0xFF2E7D32)
        ui.demandMet -> Color(0xFF1565C0)
        else         -> Color(0xFFF57F17)
    }
    Card(
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp)
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(
                "Your total possession of vehicles for ${ui.monthName}: ${ui.billedThisMonth}",
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Bold,
                color = accent
            )
            if (ui.demand > 0 || ui.target > 0) {
                LinearProgressIndicator(
                    progress = { ui.progressFraction },
                    color = accent,
                    modifier = Modifier.fillMaxWidth()
                )
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text("Demand: ${if (ui.demand > 0) ui.demand.toString() else "—"}",
                        style = MaterialTheme.typography.labelMedium)
                    Text("Target: ${if (ui.target > 0) ui.target.toString() else "—"}",
                        style = MaterialTheme.typography.labelMedium)
                }
                val note = when {
                    ui.targetMet -> "Target reached — excellent."
                    ui.demandMet -> "Demand met. ${(ui.target - ui.billedThisMonth).coerceAtLeast(0)} more to hit target."
                    ui.demand > 0 -> "${(ui.demand - ui.billedThisMonth).coerceAtLeast(0)} more to meet your demand."
                    else -> ""
                }
                if (note.isNotBlank()) {
                    Text(note, style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            } else {
                Text("No demand/target set by your admin yet.",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
private fun TaskRow(item: RepoTaskItem, onClick: () -> Unit) {
    val billed = item.billStatus.equals("billed", ignoreCase = true)
    Card(
        onClick = onClick,
        shape = RoundedCornerShape(10.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    item.vehicleNo.ifBlank { item.chassisNo.ifBlank { "—" } },
                    style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.Bold,
                    fontFamily = FontFamily.Monospace
                )
                StatusChip(billed, item.billingAction)
            }
            if (item.customerName.isNotBlank())
                Text(item.customerName, style = MaterialTheme.typography.bodySmall)
            if (item.financeName.isNotBlank() || item.branchName.isNotBlank())
                Text(
                    listOf(item.financeName, item.branchName).filter { it.isNotBlank() }.joinToString(" · "),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            Text(item.createdOn, style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.outline)
        }
    }
}

@Composable
private fun StatusChip(billed: Boolean, action: String) {
    val (label, color) = when {
        billed -> "BILLED" to Color(0xFF2E7D32)
        action.equals("hold", true)   -> "HOLD FOR COLLECTION" to Color(0xFFF57F17)
        action.equals("cancel", true) -> "CANCEL" to Color(0xFFC62828)
        else -> "OK FOR BILLING" to Color(0xFF1565C0)
    }
    Surface(shape = RoundedCornerShape(6.dp), color = color.copy(alpha = 0.12f)) {
        Text(
            label,
            style = MaterialTheme.typography.labelSmall,
            fontWeight = FontWeight.Bold,
            color = color,
            modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp)
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TaskEditSheet(
    item: RepoTaskItem,
    saving: Boolean,
    onCancel: () -> Unit,
    onSave: (RepoTaskItem) -> Unit
) {
    var loanNo      by remember(item.id) { mutableStateOf(item.loanNo) }
    var customer    by remember(item.id) { mutableStateOf(item.customerName) }
    var vehicleNo   by remember(item.id) { mutableStateOf(item.vehicleNo) }
    var model       by remember(item.id) { mutableStateOf(item.model) }
    var chassisNo   by remember(item.id) { mutableStateOf(item.chassisNo) }
    var engineNo    by remember(item.id) { mutableStateOf(item.engineNo) }
    var branch      by remember(item.id) { mutableStateOf(item.branchName) }
    var agent       by remember(item.id) { mutableStateOf(item.agentName) }
    var pyName      by remember(item.id) { mutableStateOf(item.parkingYardName) }
    var pyMobile    by remember(item.id) { mutableStateOf(item.parkingYardMobile) }
    var load        by remember(item.id) { mutableStateOf(item.loadDetails) }
    var chgNotes    by remember(item.id) { mutableStateOf(item.addlChargesNotes) }
    var chgAmount   by remember(item.id) { mutableStateOf(if (item.addlChargesAmount > 0) item.addlChargesAmount.toString() else "") }
    var confName    by remember(item.id) { mutableStateOf(item.confirmationByName) }
    var confMobile  by remember(item.id) { mutableStateOf(item.confirmationByMobile) }
    var executive   by remember(item.id) { mutableStateOf(item.executiveName) }
    var collection  by remember(item.id) { mutableStateOf(item.collectionUpdate) }
    var remark      by remember(item.id) { mutableStateOf(item.remark) }
    var action      by remember(item.id) { mutableStateOf(item.billingAction) }
    var holdDays    by remember(item.id) { mutableStateOf(if (item.holdDays > 0) item.holdDays.toString() else "") }
    var holdUntil   by remember(item.id) { mutableStateOf(item.holdUntil) }

    ModalBottomSheet(onDismissRequest = onCancel) {
        Column(
            Modifier
                .padding(horizontal = 16.dp)
                .padding(bottom = 24.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Text("Edit entry", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            if (item.billStatus.equals("billed", true)) {
                Text(
                    "This entry is already billed. Edits will show in the billing module.",
                    style = MaterialTheme.typography.labelSmall,
                    color = Color(0xFFF57F17)
                )
            }

            EditField("Loan No", loanNo) { loanNo = it }
            EditField("Customer Name", customer) { customer = it }
            EditField("Vehicle No", vehicleNo) { vehicleNo = it }
            EditField("Model / Maker", model) { model = it }
            EditField("Chassis No", chassisNo) { chassisNo = it }
            EditField("Engine No", engineNo) { engineNo = it }
            EditField("Branch", branch) { branch = it }
            EditField("Agent Name", agent) { agent = it }
            EditField("Parking Yard Name", pyName) { pyName = it }
            EditField("Parking Yard Mobile", pyMobile, KeyboardType.Phone) { pyMobile = it }
            EditField("Load Details", load) { load = it }
            EditField("Additional Charges Notes", chgNotes) { chgNotes = it }
            EditField("Additional Charges Amount", chgAmount, KeyboardType.Number) { chgAmount = it }
            EditField("Confirmation By (Name)", confName) { confName = it }
            EditField("Confirmation By (Mobile)", confMobile, KeyboardType.Phone) { confMobile = it }
            EditField("Executive Name", executive) { executive = it }
            EditField("Collection Update", collection) { collection = it }
            EditField("Remark", remark) { remark = it }

            Text("Billing decision", style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary)
            Column {
                listOf(
                    "immediate" to "OK for billing",
                    "hold"      to "Hold for collection",
                    "cancel"    to "Cancel"
                ).forEach { (v, l) ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        RadioButton(selected = action == v, onClick = { action = v })
                        Text(l, style = MaterialTheme.typography.bodyMedium)
                    }
                }
            }
            if (action == "hold") {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    OutlinedTextField(
                        value = holdDays,
                        onValueChange = { holdDays = it.filter { c -> c.isDigit() } },
                        label = { Text("Hold days") },
                        singleLine = true,
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(10.dp)
                    )
                    OutlinedTextField(
                        value = holdUntil,
                        onValueChange = { holdUntil = it },
                        label = { Text("Hold until (YYYY-MM-DD)") },
                        singleLine = true,
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(10.dp)
                    )
                }
            }

            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedButton(
                    onClick = onCancel,
                    modifier = Modifier.weight(1f).height(48.dp),
                    shape = RoundedCornerShape(10.dp)
                ) { Text("Cancel") }
                Button(
                    onClick = {
                        onSave(
                            item.copy(
                                loanNo               = loanNo,
                                customerName         = customer,
                                vehicleNo            = vehicleNo,
                                model                = model,
                                chassisNo            = chassisNo,
                                engineNo             = engineNo,
                                branchName           = branch,
                                agentName            = agent,
                                parkingYardName      = pyName,
                                parkingYardMobile    = pyMobile,
                                loadDetails          = load,
                                addlChargesNotes     = chgNotes,
                                addlChargesAmount    = chgAmount.toDoubleOrNull() ?: 0.0,
                                confirmationByName   = confName,
                                confirmationByMobile = confMobile,
                                executiveName        = executive,
                                collectionUpdate     = collection,
                                remark               = remark,
                                billingAction        = action,
                                holdUntil            = if (action == "hold") holdUntil else "",
                                holdDays             = if (action == "hold") (holdDays.toIntOrNull() ?: 0) else 0
                            )
                        )
                    },
                    enabled = !saving,
                    modifier = Modifier.weight(1f).height(48.dp),
                    shape = RoundedCornerShape(10.dp)
                ) {
                    if (saving) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    else Text("Save", fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

@Composable
private fun EditField(
    label: String,
    value: String,
    keyboard: KeyboardType = KeyboardType.Text,
    onChange: (String) -> Unit
) {
    OutlinedTextField(
        value = value,
        onValueChange = { onChange(it.uppercase()) },
        label = { Text(label) },
        keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
            keyboardType = keyboard,
            capitalization = KeyboardCapitalization.Characters
        ),
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(10.dp)
    )
}
