package com.vkenterprises.vras.ui.screens

import android.annotation.SuppressLint
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.ClickableText
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.snapshots.SnapshotStateMap
import androidx.compose.ui.*
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.withStyle
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

private data class BranchEntry(
    val branch: String,
    val financer: String,
    val createdOn: String,
    val record: SearchResult
)

private val ALL_SEL_KEYS = listOf(
    "Vehicle No", "Chassis No", "Model", "Eng No", "Agreement No",
    "Cust Name", "Cust Address", "Cust Contact",
    "BKT", "OD", "POS", "TOS", "TBR",
    "Branch", "Area", "Region",
    "Level 1", "Level 2", "Level 3", "Level 4",
    "Finance", "Head Office", "Contact 1", "Contact 2", "Contact 3",
    "Mail Id 1", "Mail Id 2", "Address", "Executive Name", "Uploaded On"
)

private fun buildAdminFields(item: SearchResult, br: SearchResult): List<Pair<String, String>> = listOf(
    "Vehicle No"     to item.vehicleNo.orEmpty(),
    "Chassis No"     to item.chassisNo.orEmpty(),
    "Model"          to item.model.orEmpty(),
    "Eng No"         to item.engineNo.orEmpty(),
    "Agreement No"   to item.agreementNo.orEmpty(),
    "Cust Name"      to item.customerName.orEmpty(),
    "Cust Address"   to item.customerAddress.orEmpty(),
    "Cust Contact"   to item.customerContact.orEmpty(),
    "BKT"            to item.bucket.orEmpty(),
    "OD"             to item.od.orEmpty(),
    "POS"            to item.pos.orEmpty(),
    "TOS"            to item.toss.orEmpty(),
    "TBR"            to item.tbrFlag.orEmpty(),
    "Branch"         to item.branchFromExcel.orEmpty(),
    "Area"           to item.area.orEmpty(),
    "Region"         to item.region.orEmpty(),
    "Level 1"        to buildLevelStr(item.level1, item.level1Contact),
    "Level 2"        to buildLevelStr(item.level2, item.level2Contact),
    "Level 3"        to buildLevelStr(item.level3, item.level3Contact),
    "Level 4"        to buildLevelStr(item.level4, item.level4Contact),
    "Finance"        to br.branchName.orEmpty(),
    "Head Office"    to br.financer.orEmpty(),
    "Contact 1"      to br.firstContact.orEmpty(),
    "Contact 2"      to br.secondContact.orEmpty(),
    "Contact 3"      to br.thirdContact.orEmpty(),
    "Mail Id 1"      to br.senderMail1.orEmpty(),
    "Mail Id 2"      to br.senderMail2.orEmpty(),
    "Address"        to br.address.orEmpty(),
    "Executive Name" to br.executiveName.orEmpty(),
    "Uploaded On"    to br.createdOn.orEmpty(),
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun VehicleDetailScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui         by searchVm.ui.collectAsState()
    val item       = ui.selectedResult
    val agentName  by authVm.userName.collectAsState(initial = "")
    val agentPhone by authVm.userMobile.collectAsState(initial = "")
    val isAdmin    by authVm.isAdmin.collectAsState(initial = false)
    val context    = LocalContext.current

    var showWaSheet      by remember { mutableStateOf(false) }
    var showCopyDialog   by remember { mutableStateOf(false) }
    var showMoreMenu     by remember { mutableStateOf(false) }
    var showSelection    by remember { mutableStateOf(false) }
    var showBranchSheet  by remember { mutableStateOf(false) }
    var selectedBranchIdx by remember { mutableStateOf(0) }
    val selChecked = remember { mutableStateMapOf<String, Boolean>() }

    // Use the un-deduplicated allResults so a vehicle found in multiple
    // finances shows every finance row in "FOUND IN FINANCES".
    val vehicleRecords = remember(item?.vehicleNo, item?.chassisNo, ui.allResults) {
        if (item == null) emptyList()
        else ui.allResults.filter { r ->
            (item.vehicleNo.isNotBlank() && r.vehicleNo == item.vehicleNo) ||
            (item.chassisNo.isNotBlank() && r.chassisNo == item.chassisNo)
        }.ifEmpty { listOf(item) }
    }

    val uniqueBranches = remember(vehicleRecords) {
        vehicleRecords.map { r ->
            BranchEntry(
                branch    = r.branchName.orEmpty().ifBlank { r.branchFromExcel.orEmpty() },
                financer  = r.financer.orEmpty(),
                createdOn = r.createdOn.orEmpty(),
                record    = r
            )
        }.distinctBy { "${it.branch}|||${it.financer}" }
         .filter { it.branch.isNotBlank() || it.financer.isNotBlank() }
         .sortedByDescending { it.createdOn }
    }

    // Auto-show the "FOUND IN FINANCES" bottom sheet for admins whenever the
    // vehicle has at least one finance — shown for single finance too.
    LaunchedEffect(isAdmin, uniqueBranches.size) {
        if (isAdmin && uniqueBranches.isNotEmpty()) showBranchSheet = true
    }

    val branchRecord: SearchResult? = uniqueBranches.getOrNull(selectedBranchIdx)?.record ?: item

    LaunchedEffect(item?.vehicleNo) {
        selectedBranchIdx = 0
        selChecked.clear()
        ALL_SEL_KEYS.forEach { selChecked[it] = true }

        if (item == null) return@LaunchedEffect
        val userId = authVm.userId.first()
        if (userId == 0L) return@LaunchedEffect

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

    if (showWaSheet && item != null) {
        ModalBottomSheet(onDismissRequest = { showWaSheet = false }) {
            Column(
                Modifier.padding(16.dp).padding(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("Send WhatsApp",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(bottom = 4.dp))
                WaOptionButton("Banker for Confirmation", Color(0xFF1565C0)) {
                    openWhatsApp(context, buildQuickWaMessage(item, "Please confirm this vehicle.", agentName, agentPhone))
                    showWaSheet = false
                }
                WaOptionButton("OK for Repo", Color(0xFF2E7D32)) {
                    openWhatsApp(context, buildQuickWaMessage(item, "Ok for repo.", agentName, agentPhone))
                    showWaSheet = false
                }
                WaOptionButton("Not Confirmed", Color(0xFFC62828)) {
                    openWhatsApp(context, buildQuickWaMessage(item, "Cancel", agentName, agentPhone))
                    showWaSheet = false
                }
            }
        }
    }

    if (showCopyDialog && item != null) {
        CopyDialog(item = item, onDismiss = { showCopyDialog = false }, context = context)
    }

    // ── FOUND IN BRANCHES bottom sheet ────────────────────────────────────────
    // Slides up from the bottom on entry (and on demand via the chip in the
    // header). Tap a card → details panel updates with that finance's data.
    if (showBranchSheet && uniqueBranches.isNotEmpty()) {
        ModalBottomSheet(onDismissRequest = { showBranchSheet = false }) {
            Column(
                Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    modifier = Modifier.padding(bottom = 4.dp)
                ) {
                    Icon(Icons.Default.AccountBalance, null,
                        tint = Color(0xFFF57F17), modifier = Modifier.size(18.dp))
                    Text(
                        "FOUND IN FINANCES",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        color = Color(0xFFF57F17)
                    )
                }
                uniqueBranches.forEachIndexed { idx, entry ->
                    val isSelected = idx == selectedBranchIdx
                    Card(
                        onClick  = {
                            selectedBranchIdx = idx
                            showBranchSheet = false
                        },
                        shape    = RoundedCornerShape(10.dp),
                        border   = if (isSelected) BorderStroke(2.dp, Color(0xFFF57F17)) else null,
                        colors   = CardDefaults.cardColors(
                            containerColor = if (isSelected) Color(0xFFFFF8E1)
                                             else MaterialTheme.colorScheme.surfaceVariant
                        ),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Column(Modifier.padding(horizontal = 12.dp, vertical = 10.dp)) {
                            Row(
                                Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                // Finance = the DB branch name (the displayed "Finance")
                                Text(
                                    entry.branch.ifBlank { "—" },
                                    style      = MaterialTheme.typography.bodyMedium,
                                    fontWeight = FontWeight.Bold,
                                    color      = MaterialTheme.colorScheme.onSurface,
                                    modifier   = Modifier.weight(1f)
                                )
                                Text(
                                    entry.createdOn,
                                    style = MaterialTheme.typography.labelSmall,
                                    fontWeight = FontWeight.SemiBold,
                                    color = Color(0xFFF57F17)
                                )
                            }
                            // Head Office = the DB finance name (financer)
                            if (entry.financer.isNotBlank()) {
                                Text(
                                    "Head Office: ${entry.financer}",
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier.padding(top = 3.dp)
                                )
                            }
                        }
                    }
                }
            }
        }
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
                            color = if (showSelection) Color(0xFF4A148C) else Color(0xFF6A1B9A),
                            modifier = Modifier.weight(1f)
                        ) {
                            val currentItem = item
                            val currentBr   = branchRecord ?: currentItem
                            if (showSelection) {
                                val fields = buildAdminFields(currentItem, currentBr)
                                val text = fields
                                    .filter { selChecked[it.first] == true && it.second.isNotBlank() }
                                    .joinToString("\n") { "${it.first}: ${it.second}" }
                                val cb = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                                cb.setPrimaryClip(ClipData.newPlainText("Vehicle Info", text))
                            } else {
                                showCopyDialog = true
                            }
                        }
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
                AdminDetailView(
                    item              = item,
                    branchRecord      = branchRecord ?: item,
                    uniqueBranches    = uniqueBranches,
                    selectedBranchIdx = selectedBranchIdx,
                    onBranchSelect    = { selectedBranchIdx = it },
                    showSelection     = showSelection,
                    onToggleSelection = { showSelection = !showSelection },
                    selChecked        = selChecked,
                    onShowBranchSheet = { showBranchSheet = true }
                )
            } else {
                BasicDetailView(item = item, agentName = agentName, agentPhone = agentPhone)
            }
            Spacer(Modifier.height(8.dp))
        }
    }
}

// ── Admin full detail view ─────────────────────────────────────────────────

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun AdminDetailView(
    item: SearchResult,
    branchRecord: SearchResult,
    uniqueBranches: List<BranchEntry>,
    selectedBranchIdx: Int,
    onBranchSelect: (Int) -> Unit,
    showSelection: Boolean,
    onToggleSelection: () -> Unit,
    selChecked: SnapshotStateMap<String, Boolean>,
    onShowBranchSheet: () -> Unit
) {
    // Top row: "Found in N branches" chip (left) + Select-fields toggle (right)
    Row(
        Modifier.fillMaxWidth().padding(bottom = 2.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        if (uniqueBranches.isNotEmpty()) {
            AssistChip(
                onClick = onShowBranchSheet,
                label = {
                    Text(
                        "Found in ${uniqueBranches.size} finance" +
                            if (uniqueBranches.size == 1) "" else "s",
                        fontWeight = FontWeight.Bold
                    )
                },
                leadingIcon = {
                    Icon(Icons.Default.AccountBalance, null,
                        tint = Color(0xFFF57F17), modifier = Modifier.size(16.dp))
                },
                colors = AssistChipDefaults.assistChipColors(
                    labelColor = Color(0xFFF57F17),
                    containerColor = Color(0xFFFFF8E1)
                )
            )
        } else {
            Spacer(Modifier)
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                "Select fields",
                style = MaterialTheme.typography.labelSmall,
                color = if (showSelection) Color(0xFF6A1B9A) else MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.width(6.dp))
            Switch(checked = showSelection, onCheckedChange = { onToggleSelection() })
        }
    }

    // All fields in one compact card
    Card(
        shape     = RoundedCornerShape(12.dp),
        colors    = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(1.dp),
        modifier  = Modifier.fillMaxWidth()
    ) {
        Column(Modifier.padding(horizontal = 12.dp, vertical = 10.dp)) {

            SRow("Vehicle No",   item.vehicleNo,   mono = true,
                invalid = !item.vehicleNo.isNullOrBlank() && !item.vehicleNo.isValidRc(),
                sel = showSelection, chk = selChecked["Vehicle No"] == true
            ) { selChecked["Vehicle No"] = it }
            SRow("Chassis No",   item.chassisNo,   mono = true,
                sel = showSelection, chk = selChecked["Chassis No"] == true
            ) { selChecked["Chassis No"] = it }
            SRow("Model",        item.model,
                sel = showSelection, chk = selChecked["Model"] == true
            ) { selChecked["Model"] = it }
            SRow("Eng No",       item.engineNo,    mono = true,
                sel = showSelection, chk = selChecked["Eng No"] == true
            ) { selChecked["Eng No"] = it }
            SRow("Agreement No", item.agreementNo, mono = true,
                sel = showSelection, chk = selChecked["Agreement No"] == true
            ) { selChecked["Agreement No"] = it }
            SRow("Cust Name",    item.customerName,
                sel = showSelection, chk = selChecked["Cust Name"] == true
            ) { selChecked["Cust Name"] = it }
            SRow("Cust Address", item.customerAddress,
                sel = showSelection, chk = selChecked["Cust Address"] == true
            ) { selChecked["Cust Address"] = it }
            SRow("Cust Contact", item.customerContact, mono = true,
                sel = showSelection, chk = selChecked["Cust Contact"] == true
            ) { selChecked["Cust Contact"] = it }

            CSep()

            SRow("BKT", item.bucket,
                sel = showSelection, chk = selChecked["BKT"] == true) { selChecked["BKT"] = it }
            SRow("OD",  item.od,
                sel = showSelection, chk = selChecked["OD"] == true)  { selChecked["OD"] = it }
            SRow("POS", item.pos,
                sel = showSelection, chk = selChecked["POS"] == true) { selChecked["POS"] = it }
            SRow("TOS", item.toss,
                sel = showSelection, chk = selChecked["TOS"] == true) { selChecked["TOS"] = it }
            SRow("TBR", item.tbrFlag,
                sel = showSelection, chk = selChecked["TBR"] == true) { selChecked["TBR"] = it }

            CSep()

            SRow("Branch", item.branchFromExcel,
                sel = showSelection, chk = selChecked["Branch"] == true) { selChecked["Branch"] = it }
            SRow("Area",   item.area,
                sel = showSelection, chk = selChecked["Area"] == true)   { selChecked["Area"] = it }
            SRow("Region", item.region,
                sel = showSelection, chk = selChecked["Region"] == true) { selChecked["Region"] = it }

            CSep()

            SRow("Level 1",         item.level1,
                sel = showSelection, chk = selChecked["Level 1"] == true) { selChecked["Level 1"] = it }
            SRow("Level 1 Contact", item.level1Contact, mono = true,
                sel = showSelection, chk = selChecked["Level 1"] == true) { selChecked["Level 1"] = it }
            SRow("Level 2",         item.level2,
                sel = showSelection, chk = selChecked["Level 2"] == true) { selChecked["Level 2"] = it }
            SRow("Level 2 Contact", item.level2Contact, mono = true,
                sel = showSelection, chk = selChecked["Level 2"] == true) { selChecked["Level 2"] = it }
            SRow("Level 3",         item.level3,
                sel = showSelection, chk = selChecked["Level 3"] == true) { selChecked["Level 3"] = it }
            SRow("Level 3 Contact", item.level3Contact, mono = true,
                sel = showSelection, chk = selChecked["Level 3"] == true) { selChecked["Level 3"] = it }
            SRow("Level 4",         item.level4,
                sel = showSelection, chk = selChecked["Level 4"] == true) { selChecked["Level 4"] = it }
            SRow("Level 4 Contact", item.level4Contact, mono = true,
                sel = showSelection, chk = selChecked["Level 4"] == true) { selChecked["Level 4"] = it }

            CSep()

            SRow("Finance",        branchRecord.branchName,
                sel = showSelection, chk = selChecked["Finance"] == true
            ) { selChecked["Finance"] = it }
            SRow("Head Office",    branchRecord.financer,
                sel = showSelection, chk = selChecked["Head Office"] == true
            ) { selChecked["Head Office"] = it }
            SRow("Contact 1",      branchRecord.firstContact,  mono = true,
                sel = showSelection, chk = selChecked["Contact 1"] == true
            ) { selChecked["Contact 1"] = it }
            SRow("Contact 2",      branchRecord.secondContact, mono = true,
                sel = showSelection, chk = selChecked["Contact 2"] == true
            ) { selChecked["Contact 2"] = it }
            SRow("Contact 3",      branchRecord.thirdContact,  mono = true,
                sel = showSelection, chk = selChecked["Contact 3"] == true
            ) { selChecked["Contact 3"] = it }
            SRow("Mail Id 1",      branchRecord.senderMail1,
                sel = showSelection, chk = selChecked["Mail Id 1"] == true
            ) { selChecked["Mail Id 1"] = it }
            SRow("Mail Id 2",      branchRecord.senderMail2,
                sel = showSelection, chk = selChecked["Mail Id 2"] == true
            ) { selChecked["Mail Id 2"] = it }
            SRow("Address",        branchRecord.address,
                sel = showSelection, chk = selChecked["Address"] == true
            ) { selChecked["Address"] = it }
            SRow("Executive Name", branchRecord.executiveName,
                sel = showSelection, chk = selChecked["Executive Name"] == true
            ) { selChecked["Executive Name"] = it }

            CSep()

            SRow("Uploaded On", branchRecord.createdOn,
                sel = showSelection, chk = selChecked["Uploaded On"] == true
            ) { selChecked["Uploaded On"] = it }
        }
    }

    // The "FOUND IN BRANCHES" list lives in a bottom sheet now — it auto-opens
    // on entry and the chip in the header re-opens it on demand. See parent.
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

// ── Compact row with optional selection checkbox ───────────────────────────

// Matches 10-digit phone numbers starting with 6/7/8/9 (Indian mobile format).
// \b ensures we don't match digits inside longer numbers.
private val PHONE_REGEX = Regex("\\b[6-9]\\d{9}\\b")

@Composable
private fun SRow(
    label: String,
    value: String?,
    mono: Boolean = false,
    invalid: Boolean = false,
    sel: Boolean = false,
    chk: Boolean = false,
    onChk: (Boolean) -> Unit = {}
) {
    val display = value.orEmpty()
    val context = LocalContext.current

    val baseColor = if (invalid) MaterialTheme.colorScheme.error
                    else if (display.isBlank()) MaterialTheme.colorScheme.outlineVariant
                    else MaterialTheme.colorScheme.onSurface
    val phoneColor = MaterialTheme.colorScheme.primary

    val phoneMatches = PHONE_REGEX.findAll(display).toList()
    val annotated = remember(display, phoneColor, baseColor) {
        buildAnnotatedString {
            var last = 0
            phoneMatches.forEach { m ->
                if (m.range.first > last) append(display.substring(last, m.range.first))
                pushStringAnnotation(tag = "PHONE", annotation = m.value)
                withStyle(SpanStyle(color = phoneColor, textDecoration = TextDecoration.Underline)) {
                    append(m.value)
                }
                pop()
                last = m.range.last + 1
            }
            if (last < display.length) append(display.substring(last))
        }
    }

    Row(
        Modifier.fillMaxWidth().padding(vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        if (sel) {
            Checkbox(
                checked = chk,
                onCheckedChange = onChk,
                modifier = Modifier.size(24.dp)
            )
            Spacer(Modifier.width(6.dp))
        }
        // Label in a fixed-width box, then the ":" as a separate element so
        // every colon lines up in one vertical column regardless of label length.
        // Force SansSerif (Roboto) so the app's typography stays consistent even
        // when the user has a custom phone font installed.
        Text(
            label,
            style      = MaterialTheme.typography.labelSmall,
            fontFamily = FontFamily.SansSerif,
            fontWeight = FontWeight.SemiBold,
            color      = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier   = Modifier.width(if (sel) 90.dp else 110.dp)
        )
        Text(
            ":",
            style      = MaterialTheme.typography.labelSmall,
            fontFamily = FontFamily.SansSerif,
            color      = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier   = Modifier.padding(end = 8.dp)
        )
        ClickableText(
            text = annotated,
            style = MaterialTheme.typography.bodySmall.copy(
                color      = baseColor,
                fontWeight = if (display.isBlank()) FontWeight.Normal else FontWeight.Bold,
                fontFamily = if (mono) FontFamily.Monospace else FontFamily.SansSerif
            ),
            onClick = { offset ->
                annotated.getStringAnnotations(tag = "PHONE", start = offset, end = offset)
                    .firstOrNull()?.let { ann ->
                        runCatching {
                            context.startActivity(
                                Intent(Intent.ACTION_DIAL, Uri.parse("tel:${ann.item}"))
                            )
                        }
                    }
            },
            modifier = Modifier.weight(1f)
        )
        if (invalid) {
            Surface(
                shape = RoundedCornerShape(4.dp),
                color = MaterialTheme.colorScheme.errorContainer
            ) {
                Text("INVALID",
                    style    = MaterialTheme.typography.labelSmall,
                    color    = MaterialTheme.colorScheme.onErrorContainer,
                    modifier = Modifier.padding(horizontal = 4.dp, vertical = 1.dp))
            }
        }
    }
}

@Composable
private fun CRow(label: String, value: String?, mono: Boolean = false, invalid: Boolean = false) {
    SRow(label, value, mono, invalid)
}

@Composable
private fun CSep() {
    HorizontalDivider(
        modifier = Modifier.padding(vertical = 6.dp),
        color    = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f)
    )
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
            style      = MaterialTheme.typography.bodySmall,
            fontFamily = FontFamily.SansSerif,
            color      = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier   = Modifier.weight(0.38f))
        Row(Modifier.weight(0.62f), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(value,
                style      = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.Bold,
                fontFamily = if (label in listOf("Vehicle No","Chassis No","Engine No"))
                    FontFamily.Monospace else FontFamily.SansSerif,
                color      = if (invalid) MaterialTheme.colorScheme.error else valueColor)
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
