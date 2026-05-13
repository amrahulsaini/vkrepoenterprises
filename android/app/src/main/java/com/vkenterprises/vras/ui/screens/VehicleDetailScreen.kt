package com.vkenterprises.vras.ui.screens

import android.annotation.SuppressLint
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
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

    LaunchedEffect(item) {
        if (item == null) return@LaunchedEffect
        val userId = authVm.userId.first()
        if (userId == 0L) return@LaunchedEffect
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
    // Branch count banner
    if (results.size > 1) {
        Card(
            shape  = RoundedCornerShape(10.dp),
            colors = CardDefaults.cardColors(containerColor = Color(0xFFFFF8E1)),
            modifier = Modifier.fillMaxWidth()
        ) {
            Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Default.AccountBalance, null, tint = Color(0xFFF57F17))
                Text("Found in ${results.size} branches",
                    fontWeight = FontWeight.SemiBold,
                    style = MaterialTheme.typography.bodyMedium,
                    color = Color(0xFFF57F17))
            }
        }
        // List branch names
        Card(
            shape  = RoundedCornerShape(10.dp),
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                results.forEachIndexed { idx, r ->
                    val branch = r.branchName.ifBlank { r.branchFromExcel.ifBlank { "Branch ${idx + 1}" } }
                    Row(verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        Surface(shape = RoundedCornerShape(4.dp),
                            color = MaterialTheme.colorScheme.primaryContainer) {
                            Text("${idx + 1}",
                                style = MaterialTheme.typography.labelSmall,
                                modifier = Modifier.padding(horizontal = 5.dp, vertical = 2.dp))
                        }
                        Text(branch, style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
        }
    }

    // Vehicle details
    InfoCard("Vehicle Details") {
        DRow("Vehicle No",    item.vehicleNo,       mono = true,
            invalid = item.vehicleNo.isNotBlank() && !item.vehicleNo.isValidRc())
        DRow("Chassis No",    item.chassisNo,       mono = true)
        DRow("Engine No",     item.engineNo,        mono = true)
        DRow("Model / Make",  item.model)
        DRow("Agreement No",  item.agreementNo)
        DRow("Cust. Name",    item.customerName)
        DRow("Cust. Address", item.customerAddress)
        DRow("Cust. Contact", item.customerContact)
    }

    // Financial
    InfoCard("Financial Info") {
        DRow("Bucket",    item.bucket)
        DRow("GV",        item.gv)
        DRow("OD",        item.od)
        DRow("Seasoning", item.seasoning)
        DRow("TBR Flag",  item.tbrFlag)
        DRow("Region",    item.region)
        DRow("Area",      item.area)
        DRow("Remark",    item.remark)
    }

    // Branch & Finance
    InfoCard("Branch & Finance") {
        DRow("Branch (xlsx)", item.branchFromExcel)
        DRow("Branch",        item.branchName)
        DRow("Finance",       item.financer)
        DRow("Contact 1",     item.firstContact)
        DRow("Contact 2",     item.secondContact)
        DRow("Contact 3",     item.thirdContact)
    }

    // Levels
    InfoCard("Level Contacts") {
        if (item.level1.isNotBlank() || item.level1Contact.isNotBlank())
            DRow("Level 1", "${item.level1}  ${item.level1Contact}".trim())
        if (item.level2.isNotBlank() || item.level2Contact.isNotBlank())
            DRow("Level 2", "${item.level2}  ${item.level2Contact}".trim())
        if (item.level3.isNotBlank() || item.level3Contact.isNotBlank())
            DRow("Level 3", "${item.level3}  ${item.level3Contact}".trim())
        if (item.level4.isNotBlank() || item.level4Contact.isNotBlank())
            DRow("Level 4", "${item.level4}  ${item.level4Contact}".trim())
    }

    // Additional
    InfoCard("Additional Info") {
        DRow("Sec9 Available",  item.sec9)
        DRow("Sec17 Available", item.sec17)
        DRow("Executive",       item.executiveName)
        DRow("POS",             item.pos)
        DRow("TOSS",            item.toss)
        DRow("Mail 1",          item.senderMail1)
        DRow("Mail 2",          item.senderMail2)
        DRow("Uploaded On",     item.createdOn)
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
            "Vehicle No"       to item.vehicleNo,
            "Chassis No"       to item.chassisNo,
            "Engine No"        to item.engineNo,
            "Model / Make"     to item.model,
            "Agreement No"     to item.agreementNo,
            "Cust. Name"       to item.customerName,
            "Cust. Address"    to item.customerAddress,
            "Cust. Contact"    to item.customerContact,
            "Bucket"           to item.bucket,
            "GV"               to item.gv,
            "OD"               to item.od,
            "Seasoning"        to item.seasoning,
            "TBR Flag"         to item.tbrFlag,
            "Region"           to item.region,
            "Area"             to item.area,
            "Branch (xlsx)"    to item.branchFromExcel,
            "Branch"           to item.branchName,
            "Finance"          to item.financer,
            "Contact 1"        to item.firstContact,
            "Contact 2"        to item.secondContact,
            "Contact 3"        to item.thirdContact,
            "Level 1"          to "${item.level1} - ${item.level1Contact}".trim(' ', '-'),
            "Level 2"          to "${item.level2} - ${item.level2Contact}".trim(' ', '-'),
            "Level 3"          to "${item.level3} - ${item.level3Contact}".trim(' ', '-'),
            "Level 4"          to "${item.level4} - ${item.level4Contact}".trim(' ', '-'),
            "Sec9 Available"   to item.sec9,
            "Sec17 Available"  to item.sec17,
            "Executive"        to item.executiveName,
            "POS"              to item.pos,
            "TOSS"             to item.toss,
            "Mail 1"           to item.senderMail1,
            "Mail 2"           to item.senderMail2,
            "Remark"           to item.remark,
            "Uploaded On"      to item.createdOn,
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

private fun buildQuickWaMessage(
    item: SearchResult, status: String,
    agentName: String, agentPhone: String
): String = buildString {
    appendLine("*Respected sir,*")
    appendLine("Loan No: *${item.agreementNo.ifBlank { "-" }}*")
    appendLine("Customer Name: *${item.customerName.ifBlank { "-" }}*")
    appendLine("Branch: *${item.branchName.ifBlank { "-" }}*")
    appendLine("Vehicle No: *${item.vehicleNo}*")
    appendLine("Model/Maker: *${item.model.ifBlank { "-" }}*")
    appendLine("Chassis No: *${item.chassisNo}*")
    appendLine("Engine No: *${item.engineNo.ifBlank { "-" }}*")
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
private fun DRow(label: String, value: String, mono: Boolean = false, invalid: Boolean = false) {
    if (value.isBlank()) return
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically) {
        Text(label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.38f))
        Row(Modifier.weight(0.62f), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(value,
                style = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.SemiBold,
                fontFamily = if (mono) FontFamily.Monospace else FontFamily.Default,
                color = if (invalid) MaterialTheme.colorScheme.error
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
    label: String, value: String,
    valueColor: androidx.compose.ui.graphics.Color = MaterialTheme.colorScheme.onSurface,
    invalid: Boolean = false
) {
    if (value.isBlank()) return
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
