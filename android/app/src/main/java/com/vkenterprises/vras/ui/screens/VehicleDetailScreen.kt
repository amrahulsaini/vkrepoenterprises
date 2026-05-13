package com.vkenterprises.vras.ui.screens

import android.annotation.SuppressLint
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.models.SearchLogRequest
import com.vkenterprises.vras.data.models.SearchResult
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthViewModel
import com.vkenterprises.vras.viewmodel.SearchViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

private val RC_REGEX = Regex("^[A-Z]{2}[0-9]{2}[A-Z]{1,3}[0-9]{4}$")
private fun String.isValidRc(): Boolean =
    replace(Regex("[^A-Z0-9]"), "").uppercase().matches(RC_REGEX)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun VehicleDetailScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui          by searchVm.ui.collectAsState()
    val item        = ui.selectedResult
    val agentName   by authVm.userName.collectAsState(initial = "")
    val agentPhone  by authVm.userMobile.collectAsState(initial = "")
    val isAdmin     by authVm.isAdmin.collectAsState(initial = false)
    val context     = LocalContext.current

    // Dialog / sheet visibility state
    var showWaSheet   by remember { mutableStateOf(false) }
    var showCopyDialog by remember { mutableStateOf(false) }
    var showMoreMenu  by remember { mutableStateOf(false) }

    LaunchedEffect(item?.vehicleNo) {
        if (item == null) return@LaunchedEffect
        val userId = authVm.userId.first()
        if (userId == 0L) return@LaunchedEffect

        // Admin: if data came from local cache (most fields blank), fetch full details from server
        if (isAdmin && item.agreementNo.isBlank()) {
            searchVm.refetchSelectedFromServer(userId)
        }

        val loc     = getLocationOnce(context)
        val address = reverseGeocode(context, loc?.latitude, loc?.longitude)
        runCatching {
            ApiClient.api.logSearch(
                SearchLogRequest(
                    userId        = userId,
                    vehicleNo     = item.vehicleNo,
                    chassisNo     = item.chassisNo,
                    model         = item.model,
                    lat           = loc?.latitude,
                    lng           = loc?.longitude,
                    address       = address,
                    deviceTimeIso = java.time.Instant.now().toString()
                )
            )
        }
    }

    // WhatsApp quick-send bottom sheet
    if (showWaSheet && item != null) {
        ModalBottomSheet(onDismissRequest = { showWaSheet = false }) {
            Column(Modifier.padding(16.dp).padding(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Send WhatsApp",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(bottom = 4.dp))
                WaOptionButton(
                    label = "Banker for Confirmation",
                    color = Color(0xFF1565C0)
                ) {
                    openWhatsApp(context, buildQuickWaMessage(item, "Please confirm this vehicle.", agentName, agentPhone))
                    showWaSheet = false
                }
                WaOptionButton(
                    label = "OK for Repo",
                    color = Color(0xFF2E7D32)
                ) {
                    openWhatsApp(context, buildQuickWaMessage(item, "Ok for repo.", agentName, agentPhone))
                    showWaSheet = false
                }
                WaOptionButton(
                    label = "Not Confirmed",
                    color = Color(0xFFC62828)
                ) {
                    openWhatsApp(context, buildQuickWaMessage(item, "Cancel", agentName, agentPhone))
                    showWaSheet = false
                }
            }
        }
    }

    // Copy dialog
    if (showCopyDialog && item != null) {
        CopyDialog(item = item, onDismiss = { showCopyDialog = false }, context = context)
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(item?.vehicleNo ?: "Vehicle Detail", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, null)
                    }
                }
            )
        },
        bottomBar = {
            if (isAdmin && item != null) {
                Surface(shadowElevation = 8.dp, color = MaterialTheme.colorScheme.surface) {
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .navigationBarsPadding()
                            .padding(horizontal = 12.dp, vertical = 10.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        ActionChip(
                            label = "Confirm",
                            icon  = Icons.Default.CheckCircle,
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.weight(1f)
                        ) {
                            searchVm.setActionType("confirm")
                            nav.navigate(Screen.Confirm.route)
                        }
                        ActionChip(
                            label = "WhatsApp",
                            icon  = Icons.Default.Chat,
                            color = Color(0xFF25D366),
                            modifier = Modifier.weight(1f)
                        ) { showWaSheet = true }
                        ActionChip(
                            label = "OK Repo",
                            icon  = Icons.Default.Done,
                            color = Color(0xFF1565C0),
                            modifier = Modifier.weight(1f)
                        ) {
                            searchVm.setActionType("okrepo")
                            nav.navigate(Screen.Confirm.route)
                        }
                        ActionChip(
                            label = "Copy",
                            icon  = Icons.Default.ContentCopy,
                            color = Color(0xFF6A1B9A),
                            modifier = Modifier.weight(1f)
                        ) { showCopyDialog = true }
                        Box(Modifier.weight(1f)) {
                            ActionChip(
                                label    = "More",
                                icon     = Icons.Default.MoreVert,
                                color    = MaterialTheme.colorScheme.secondary,
                                modifier = Modifier.fillMaxWidth()
                            ) { showMoreMenu = true }
                            DropdownMenu(
                                expanded = showMoreMenu,
                                onDismissRequest = { showMoreMenu = false }
                            ) {
                                DropdownMenuItem(
                                    text = { Text("Cancel") },
                                    leadingIcon = { Icon(Icons.Default.Cancel, null) },
                                    onClick = {
                                        showMoreMenu = false
                                        searchVm.setActionType("cancel")
                                        nav.navigate(Screen.Confirm.route)
                                    }
                                )
                                DropdownMenuItem(
                                    text = { Text("POS (coming soon)", color = MaterialTheme.colorScheme.onSurfaceVariant) },
                                    leadingIcon = { Icon(Icons.Default.PointOfSale, null, tint = MaterialTheme.colorScheme.onSurfaceVariant) },
                                    onClick = { showMoreMenu = false },
                                    enabled = false
                                )
                            }
                        }
                    }
                }
            }
        },
        floatingActionButton = {
            if (!isAdmin && item != null) {
                ExtendedFloatingActionButton(
                    onClick = {
                        searchVm.setActionType("confirm")
                        nav.navigate(Screen.Confirm.route)
                    },
                    icon = { Icon(Icons.Default.Send, null) },
                    text = { Text("Send Confirm", fontWeight = FontWeight.Bold) },
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor   = MaterialTheme.colorScheme.onPrimary
                )
            }
        }
    ) { pad ->
        if (item == null) {
            Box(Modifier.fillMaxSize().padding(pad), contentAlignment = Alignment.Center) {
                Text("No vehicle selected.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            return@Scaffold
        }

        Column(
            Modifier
                .padding(pad)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            if (isAdmin) {
                AdminDetailView(item = item, results = ui.results)
            } else {
                BasicDetailView(item = item, agentName = agentName, agentPhone = agentPhone)
            }
            Spacer(Modifier.height(8.dp))
        }
    }
}

// ── Admin full detail view ─────────────────────────────────────────────────

@Composable
private fun AdminDetailView(item: SearchResult, results: List<SearchResult>) {
    // Deduplicate branches by (branchName, financer) so the same branch uploaded
    // multiple times only appears once. Keep all unique (branch, financer) pairs.
    data class BranchEntry(val branch: String, val financer: String)
    // Only show branches for THIS vehicle — filter by vehicleNo or chassisNo match
    val vehicleRecords = results.filter { r ->
        (item.vehicleNo.isNotBlank() && r.vehicleNo == item.vehicleNo) ||
        (item.chassisNo.isNotBlank() && r.chassisNo == item.chassisNo)
    }.ifEmpty { listOf(item) }  // fallback to the item itself if no matches
    val uniqueBranches = vehicleRecords
        .map { r ->
            val bn  = r.branchName.orEmpty().ifBlank { r.branchFromExcel.orEmpty() }
            val fin = r.financer.orEmpty()
            BranchEntry(bn, fin)
        }
        .distinctBy { "${it.branch}|||${it.financer}" }
        .filter { it.branch.isNotBlank() || it.financer.isNotBlank() }

    if (uniqueBranches.isNotEmpty()) {
        Card(
            shape  = RoundedCornerShape(10.dp),
            colors = CardDefaults.cardColors(containerColor = Color(0xFFFFF8E1)),
            modifier = Modifier.fillMaxWidth()
        ) {
            Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Default.AccountBalance, null, tint = Color(0xFFF57F17))
                Text(
                    "Found in ${uniqueBranches.size} branch${if (uniqueBranches.size == 1) "" else "es"}",
                    fontWeight = FontWeight.SemiBold,
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(0xFFF57F17))
            }
        }
        Card(
            shape  = RoundedCornerShape(10.dp),
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                uniqueBranches.forEachIndexed { idx, entry ->
                    Row(
                        verticalAlignment = Alignment.Top,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Surface(
                            shape = RoundedCornerShape(4.dp),
                            color = MaterialTheme.colorScheme.primaryContainer
                        ) {
                            Text(
                                "${idx + 1}",
                                style = MaterialTheme.typography.labelSmall,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 3.dp)
                            )
                        }
                        Column(Modifier.weight(1f)) {
                            // Financer = primary (bold, larger) — matches desktop layout
                            Text(
                                entry.financer.ifBlank { "—" },
                                style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.Medium,
                                color = MaterialTheme.colorScheme.onSurface
                            )
                            // Branch name = secondary (caption)
                            Text(
                                entry.branch.ifBlank { "—" },
                                style = MaterialTheme.typography.labelSmall,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                    if (idx < uniqueBranches.lastIndex)
                        HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.4f))
                }
            }
        }
    }

    // ── 1. Vehicle Details ──────────────────────────────────────────────
    InfoCard("Vehicle Details") {
        DRow("Vehicle No",       item.vehicleNo, mono = true,
            invalid = !item.vehicleNo.isNullOrBlank() && !item.vehicleNo.isValidRc())
        DRow("Chassis No",       item.chassisNo,       mono = true)
        DRow("Model / Make",     item.model)
        DRow("Engine No",        item.engineNo,        mono = true)
        DRow("Agreement No",     item.agreementNo,     mono = true)
        DRow("Cust. Name",       item.customerName)
        DRow("Cust. Address",    item.customerAddress)
        DRow("Cust Contact Nos", item.customerContact)
    }

    // ── 2. Key Numbers ──────────────────────────────────────────────────
    InfoCard("Key Numbers") {
        DRow("Bucket",    item.bucket)
        DRow("GV",        item.gv)
        DRow("OD",        item.od)
    }

    // ── 3. POS / TOSS ───────────────────────────────────────────────────
    InfoCard("POS / TOSS") {
        DRow("POS",  item.pos)
        DRow("TOSS", item.toss)
    }

    // ── 4. Branch ───────────────────────────────────────────────────────
    InfoCard("Branch") {
        DRow("Branch",        item.branchName)
        DRow("Branch (xlsx)", item.branchFromExcel)
    }

    // ── 5. Region & Area ────────────────────────────────────────────────
    InfoCard("Region & Area") {
        DRow("Region", item.region)
        DRow("Area",   item.area)
    }

    // ── 6. Level Contacts ───────────────────────────────────────────────
    InfoCard("Level Contacts") {
        DRow("Level 1",          item.level1)
        DRow("Level 1 Contact",  item.level1Contact, mono = true)
        DRow("Level 2",          item.level2)
        DRow("Level 2 Contact",  item.level2Contact, mono = true)
        DRow("Level 3",          item.level3)
        DRow("Level 3 Contact",  item.level3Contact, mono = true)
        DRow("Level 4",          item.level4)
        DRow("Level 4 Contact",  item.level4Contact, mono = true)
    }

    // ── 7. Finance ──────────────────────────────────────────────────────
    InfoCard("Finance") {
        DRow("Finance", item.financer)
        DRow("Address", item.address)
    }

    // ── 8. Contacts ─────────────────────────────────────────────────────
    InfoCard("Contacts") {
        DRow("Contact 1", item.firstContact, mono = true)
        DRow("Contact 2", item.secondContact, mono = true)
        DRow("Contact 3", item.thirdContact,  mono = true)
    }

    // ── 9. Compliance ───────────────────────────────────────────────────
    InfoCard("Compliance") {
        DRow("Sec9 Available",  item.sec9)
        DRow("Sec17 Available", item.sec17)
        DRow("TBR Flag",        item.tbrFlag)
        DRow("Seasoning",       item.seasoning)
    }

    // ── 10. Executive & Mails ───────────────────────────────────────────
    InfoCard("Executive & Mails") {
        DRow("Executive Name", item.executiveName)
        DRow("Mail Id 1",      item.senderMail1)
        DRow("Mail Id 2",      item.senderMail2)
    }

    // ── 11. Remark ──────────────────────────────────────────────────────
    InfoCard("Remark") {
        DRow("Remark", item.remark)
    }

    // ── 12. Meta ────────────────────────────────────────────────────────
    InfoCard("Uploaded On") {
        DRow("Uploaded On", item.createdOn)
    }
}

// ── Non-admin basic view ───────────────────────────────────────────────────

@Composable
private fun BasicDetailView(item: SearchResult, agentName: String, agentPhone: String) {
    Card(
        shape  = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(2.dp),
        modifier  = Modifier.fillMaxWidth()
    ) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Vehicle Information",
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary)
            HorizontalDivider(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.3f))
            DetailRow("Vehicle No",   item.vehicleNo,
                invalid = item.vehicleNo.isNotBlank() && !item.vehicleNo.isValidRc())
            DetailRow("Chassis No",   item.chassisNo)
            DetailRow("Engine No",    item.engineNo)
            DetailRow("Model / Make", item.model)
            DetailRow("Customer",     item.customerName)
        }
    }
    Card(
        shape  = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Agency",
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onPrimaryContainer)
            HorizontalDivider(color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.2f))
            DetailRow("Name",   "V K Enterprises", valueColor = MaterialTheme.colorScheme.onPrimaryContainer)
            if (agentName.isNotBlank())
                DetailRow("Agent",  agentName, valueColor = MaterialTheme.colorScheme.onPrimaryContainer)
            if (agentPhone.isNotBlank())
                DetailRow("Mobile", agentPhone, valueColor = MaterialTheme.colorScheme.onPrimaryContainer)
        }
    }
}

// ── Copy dialog ────────────────────────────────────────────────────────────

@Composable
private fun CopyDialog(item: SearchResult, onDismiss: () -> Unit, context: Context) {
    val fields = remember {
        listOf(
            "Vehicle No"       to item.vehicleNo.orEmpty(),
            "Chassis No"       to item.chassisNo.orEmpty(),
            "Engine No"        to item.engineNo.orEmpty(),
            "Model / Make"     to item.model.orEmpty(),
            "Agreement No"     to item.agreementNo.orEmpty(),
            "Cust. Name"       to item.customerName.orEmpty(),
            "Cust. Address"    to item.customerAddress.orEmpty(),
            "Cust. Contact"    to item.customerContact.orEmpty(),
            "Bucket"           to item.bucket.orEmpty(),
            "GV"               to item.gv.orEmpty(),
            "OD"               to item.od.orEmpty(),
            "Seasoning"        to item.seasoning.orEmpty(),
            "TBR Flag"         to item.tbrFlag.orEmpty(),
            "Region"           to item.region.orEmpty(),
            "Area"             to item.area.orEmpty(),
            "Branch (xlsx)"    to item.branchFromExcel.orEmpty(),
            "Branch"           to item.branchName.orEmpty(),
            "Finance"          to item.financer.orEmpty(),
            "Contact 1"        to item.firstContact.orEmpty(),
            "Contact 2"        to item.secondContact.orEmpty(),
            "Contact 3"        to item.thirdContact.orEmpty(),
            "Level 1"          to "${item.level1.orEmpty()} - ${item.level1Contact.orEmpty()}".trim(' ', '-'),
            "Level 2"          to "${item.level2.orEmpty()} - ${item.level2Contact.orEmpty()}".trim(' ', '-'),
            "Level 3"          to "${item.level3.orEmpty()} - ${item.level3Contact.orEmpty()}".trim(' ', '-'),
            "Level 4"          to "${item.level4.orEmpty()} - ${item.level4Contact.orEmpty()}".trim(' ', '-'),
            "Sec9 Available"   to item.sec9.orEmpty(),
            "Sec17 Available"  to item.sec17.orEmpty(),
            "Executive"        to item.executiveName.orEmpty(),
            "POS"              to item.pos.orEmpty(),
            "TOSS"             to item.toss.orEmpty(),
            "Mail 1"           to item.senderMail1.orEmpty(),
            "Mail 2"           to item.senderMail2.orEmpty(),
            "Remark"           to item.remark.orEmpty(),
            "Uploaded On"      to item.createdOn.orEmpty(),
        ).filter { it.second.isNotBlank() }
    }
    val checked = remember { mutableStateMapOf<String, Boolean>().apply { fields.forEach { put(it.first, true) } } }
    val allChecked = checked.values.all { it }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Copy Fields", fontWeight = FontWeight.Bold) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                Row(verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.fillMaxWidth()) {
                    Checkbox(checked = allChecked, onCheckedChange = { v ->
                        fields.forEach { checked[it.first] = v }
                    })
                    Text("Select All", fontWeight = FontWeight.SemiBold,
                        style = MaterialTheme.typography.bodyMedium)
                }
                HorizontalDivider(Modifier.padding(vertical = 4.dp))
                fields.forEach { (label, value) ->
                    Row(verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.fillMaxWidth()) {
                        Checkbox(
                            checked = checked[label] == true,
                            onCheckedChange = { checked[label] = it }
                        )
                        Column(Modifier.weight(1f)) {
                            Text(label, style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                            Text(value, style = MaterialTheme.typography.bodySmall,
                                fontWeight = FontWeight.Medium)
                        }
                    }
                }
            }
        },
        confirmButton = {
            Button(onClick = {
                val text = fields
                    .filter { checked[it.first] == true }
                    .joinToString("\n") { "${it.first}: ${it.second}" }
                val cb = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                cb.setPrimaryClip(ClipData.newPlainText("Vehicle Info", text))
                onDismiss()
            }) { Text("Copy") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        }
    )
}

// ── Helpers ────────────────────────────────────────────────────────────────

private fun buildLevelStr(name: String?, contact: String?): String {
    val n = name.orEmpty().trim()
    val c = contact.orEmpty().trim()
    return when {
        n.isNotBlank() && c.isNotBlank() -> "$n  |  $c"
        n.isNotBlank() -> n
        c.isNotBlank() -> c
        else -> ""
    }
}

private fun buildQuickWaMessage(
    item: SearchResult, status: String,
    agentName: String, agentPhone: String
): String = buildString {
    appendLine("*Respected sir,*")
    appendLine("Loan No: *${item.agreementNo.orEmpty().ifBlank { "-" }}*")
    appendLine("Customer Name: *${item.customerName.orEmpty().ifBlank { "-" }}*")
    appendLine("Branch: *${item.branchName.orEmpty().ifBlank { "-" }}*")
    appendLine("Vehicle No: *${item.vehicleNo.orEmpty()}*")
    appendLine("Model/Maker: *${item.model.orEmpty().ifBlank { "-" }}*")
    appendLine("Chassis No: *${item.chassisNo.orEmpty()}*")
    appendLine("Engine No: *${item.engineNo.orEmpty().ifBlank { "-" }}*")
    appendLine("Status: *$status*")
    appendLine()
    if (agentName.isNotBlank() && agentPhone.isNotBlank())
        appendLine("$agentName - $agentPhone")
    append("Agency Name: *V K Enterprises*")
}

private fun openWhatsApp(context: Context, message: String) {
    val intent = Intent(Intent.ACTION_VIEW,
        Uri.parse("https://wa.me/?text=${Uri.encode(message)}"))
    context.startActivity(intent)
}

@Composable
private fun InfoCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(
        shape  = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(1.dp),
        modifier  = Modifier.fillMaxWidth()
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(title,
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary)
            HorizontalDivider(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.2f))
            content()
        }
    }
}

@Composable
private fun DRow(label: String, value: String?, mono: Boolean = false, invalid: Boolean = false) {
    val display = value.orEmpty()
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically) {
        Text(label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.38f))
        Row(Modifier.weight(0.62f), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(
                display,
                style = MaterialTheme.typography.bodySmall,
                fontWeight = if (display.isBlank()) FontWeight.Normal else FontWeight.SemiBold,
                fontFamily = if (mono) FontFamily.Monospace else FontFamily.Default,
                color = if (invalid) MaterialTheme.colorScheme.error
                        else if (display.isBlank()) MaterialTheme.colorScheme.outlineVariant
                        else MaterialTheme.colorScheme.onSurface)
            if (invalid) {
                Surface(shape = RoundedCornerShape(4.dp),
                    color = MaterialTheme.colorScheme.errorContainer) {
                    Text("INVALID",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        modifier = Modifier.padding(horizontal = 4.dp, vertical = 1.dp))
                }
            }
        }
    }
}

@Composable
private fun ActionChip(
    label: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    color: Color,
    modifier: Modifier = Modifier,
    onClick: () -> Unit
) {
    Button(
        onClick = onClick,
        modifier = modifier.height(40.dp),
        shape  = RoundedCornerShape(8.dp),
        colors = ButtonDefaults.buttonColors(containerColor = color, contentColor = Color.White),
        contentPadding = PaddingValues(horizontal = 6.dp)
    ) {
        Icon(icon, null, modifier = Modifier.size(14.dp))
        Spacer(Modifier.width(3.dp))
        Text(label, fontSize = 11.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
    }
}

@Composable
private fun WaOptionButton(label: String, color: Color, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth().height(48.dp),
        shape  = RoundedCornerShape(10.dp),
        colors = ButtonDefaults.buttonColors(containerColor = color, contentColor = Color.White)
    ) {
        Icon(Icons.Default.Chat, null, Modifier.size(16.dp))
        Spacer(Modifier.width(8.dp))
        Text(label, fontWeight = FontWeight.SemiBold)
    }
}

// ── Old DetailRow (kept for BasicDetailView) ───────────────────────────────

@Composable
private fun DetailRow(
    label: String, value: String?,
    valueColor: androidx.compose.ui.graphics.Color = MaterialTheme.colorScheme.onSurface,
    invalid: Boolean = false
) {
    if (value.isNullOrBlank()) return
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically) {
        Text(label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.38f))
        Row(Modifier.weight(0.62f), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(value,
                style = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.SemiBold,
                fontFamily = if (label in listOf("Vehicle No","Chassis No","Engine No"))
                    FontFamily.Monospace else FontFamily.Default,
                color = if (invalid) MaterialTheme.colorScheme.error else valueColor)
            if (invalid) {
                Surface(shape = RoundedCornerShape(4.dp),
                    color = MaterialTheme.colorScheme.errorContainer) {
                    Text("INVALID",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        modifier = Modifier.padding(horizontal = 5.dp, vertical = 2.dp))
                }
            }
        }
    }
}

// ── Location helpers ───────────────────────────────────────────────────────

private suspend fun reverseGeocode(context: Context, lat: Double?, lng: Double?): String? {
    if (lat == null || lng == null) return null
    val fromGeocoder = withContext(Dispatchers.IO) {
        runCatching {
            if (!android.location.Geocoder.isPresent()) return@runCatching null
            val gc = android.location.Geocoder(context, java.util.Locale.getDefault())
            @Suppress("DEPRECATION")
            gc.getFromLocation(lat, lng, 1)?.firstOrNull()?.getAddressLine(0)
        }.getOrNull()
    }
    if (!fromGeocoder.isNullOrBlank()) return fromGeocoder
    return withContext(Dispatchers.IO) {
        runCatching {
            val conn = java.net.URL(
                "https://nominatim.openstreetmap.org/reverse?lat=$lat&lon=$lng&format=json&zoom=16&accept-language=en"
            ).openConnection() as java.net.HttpURLConnection
            conn.setRequestProperty("User-Agent", "VKRepoCar/1.0")
            conn.connectTimeout = 6000
            conn.readTimeout    = 6000
            val json = conn.inputStream.bufferedReader().readText()
            conn.disconnect()
            org.json.JSONObject(json).optString("display_name").takeIf { it.isNotBlank() }
        }.getOrNull()
    }
}

@SuppressLint("MissingPermission")
private suspend fun getLocationOnce(context: Context): android.location.Location? =
    suspendCancellableCoroutine { cont ->
        val fused = LocationServices.getFusedLocationProviderClient(context)
        fused.getCurrentLocation(Priority.PRIORITY_HIGH_ACCURACY, null)
            .addOnSuccessListener { loc ->
                if (loc != null) { cont.resume(loc); return@addOnSuccessListener }
                fused.lastLocation
                    .addOnSuccessListener { last -> cont.resume(last) }
                    .addOnFailureListener { cont.resume(null) }
            }
            .addOnFailureListener {
                fused.lastLocation
                    .addOnSuccessListener { last -> cont.resume(last) }
                    .addOnFailureListener { cont.resume(null) }
            }
    }
