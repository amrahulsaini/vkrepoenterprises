package com.vkenterprises.crmrs.ui.screens

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.vkenterprises.crmrs.data.models.HeadOffice
import com.vkenterprises.crmrs.data.models.SaveRepoSettingsRequest
import com.vkenterprises.crmrs.navigation.Screen
import com.vkenterprises.crmrs.utils.RepoDocType
import com.vkenterprises.crmrs.utils.RepoDocx
import com.vkenterprises.crmrs.utils.RepoPdf
import com.vkenterprises.crmrs.utils.compressImageToBase64
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.RepoViewModel
import com.vkenterprises.crmrs.viewmodel.SearchMode
import coil.compose.AsyncImage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RepoTypeScreen(
    repoVm: RepoViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val userId by authVm.userId.collectAsState(initial = -1L)
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Repossession Letter", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                }
            )
        }
    ) { pad ->
        Column(
            Modifier.padding(pad).fillMaxSize().padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Text("Choose the letter to generate",
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)

            RepoBigTile(
                title = "PRE-REPOSSESSION",
                subtitle = "Pre-Repo intimation to police station",
                icon = Icons.Default.Description,
                accent = MaterialTheme.colorScheme.primary,
                enabled = true
            ) {
                if (userId > 0) repoVm.loadHeadOffices(userId)
                nav.navigate(Screen.RepoHeadOffices.route)
            }

            RepoBigTile(
                title = "POST-REPOSSESSION",
                subtitle = "Post-Repo intimation to police station",
                icon = Icons.Default.AssignmentTurnedIn,
                accent = Color(0xFF6A1B9A),
                enabled = true
            ) {
                if (userId > 0) repoVm.loadHeadOffices(userId)
                nav.navigate(Screen.RepoHeadOffices.route)
            }
        }
    }
}

@Composable
private fun RepoBigTile(
    title: String,
    subtitle: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    accent: Color,
    enabled: Boolean,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        enabled = enabled,
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (enabled) MaterialTheme.colorScheme.surfaceVariant
                             else MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)
        ),
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            Modifier.padding(18.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Surface(shape = RoundedCornerShape(12.dp), color = accent.copy(alpha = 0.15f),
                modifier = Modifier.size(52.dp)) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(icon, null, tint = accent, modifier = Modifier.size(28.dp))
                }
            }
            Column(Modifier.weight(1f)) {
                Text(title, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
                Text(subtitle, style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (enabled) Icon(Icons.Default.ChevronRight, null, tint = MaterialTheme.colorScheme.outline)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RepoHeadOfficeScreen(
    repoVm: RepoViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui by repoVm.ui.collectAsState()
    val userId by authVm.userId.collectAsState(initial = -1L)
    var filter by remember { mutableStateOf("") }

    LaunchedEffect(userId) {
        if (userId > 0 && ui.headOffices.isEmpty() && !ui.loadingHeadOffices) repoVm.loadHeadOffices(userId)
    }

    val shown = remember(ui.headOffices, filter) {
        if (filter.isBlank()) ui.headOffices
        else ui.headOffices.filter { it.name.contains(filter, ignoreCase = true) }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Select Head Office", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                }
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {
            OutlinedTextField(
                value = filter,
                onValueChange = { filter = it },
                placeholder = { Text("Search head office") },
                leadingIcon = { Icon(Icons.Default.Search, null) },
                singleLine = true,
                shape = RoundedCornerShape(10.dp),
                modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp)
            )

            when {
                ui.loadingHeadOffices -> Box(Modifier.fillMaxSize(), Alignment.Center) { CircularProgressIndicator() }
                ui.headOfficeError != null -> Box(Modifier.fillMaxSize().padding(24.dp), Alignment.Center) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(ui.headOfficeError!!, color = MaterialTheme.colorScheme.error)
                        Spacer(Modifier.height(12.dp))
                        Button(onClick = { if (userId > 0) repoVm.loadHeadOffices(userId) }) { Text("Retry") }
                    }
                }
                shown.isEmpty() -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    Text("No head offices found.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                else -> LazyColumn(Modifier.fillMaxSize()) {
                    items(shown, key = { it.id }) { ho ->
                        HeadOfficeRow(ho) {
                            repoVm.selectHeadOffice(ho, userId)
                            nav.navigate(Screen.RepoSearch.route)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun HeadOfficeRow(ho: HeadOffice, onClick: () -> Unit) {
    Card(
        onClick = onClick,
        shape = RoundedCornerShape(0.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            Modifier.padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(Icons.Default.AccountBalance, null, tint = Color(0xFFF57F17),
                modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(ho.name.uppercase(), fontWeight = FontWeight.Bold,
                    style = MaterialTheme.typography.bodyMedium,
                    maxLines = 2, overflow = TextOverflow.Ellipsis)
                Text("${ho.totalRecords} records",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Icon(Icons.Default.ChevronRight, null, tint = MaterialTheme.colorScheme.outline)
        }
    }
    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.4f), thickness = 0.5.dp)
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RepoSearchScreen(
    repoVm: RepoViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui by repoVm.ui.collectAsState()
    val userId by authVm.userId.collectAsState(initial = -1L)
    val maxLen = if (ui.mode == SearchMode.RC) 4 else 5
    val hasResults = ui.results.isNotEmpty()

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("Search Vehicle", fontWeight = FontWeight.Bold, fontSize = 16.sp)
                        ui.selectedHeadOffice?.let {
                            Text(it.name.uppercase(), style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                    }
                },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                }
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {

            if (!hasResults) Spacer(Modifier.weight(1f))

            Surface(color = MaterialTheme.colorScheme.surfaceVariant, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(horizontal = 12.dp, vertical = 10.dp)) {
                    if (!hasResults) {
                        Text("Enter the last $maxLen digits of the ${if (ui.mode == SearchMode.RC) "RC" else "Chassis"}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(bottom = 8.dp))
                    }
                    OutlinedTextField(
                        value = ui.inputText,
                        onValueChange = { repoVm.onInputChange(it, userId) },
                        placeholder = {
                            Text(if (ui.mode == SearchMode.RC) "Last 4 digits of RC" else "Last 5 digits of Chassis",
                                style = MaterialTheme.typography.bodySmall)
                        },
                        leadingIcon = { Icon(Icons.Default.Search, null, Modifier.size(18.dp)) },
                        trailingIcon = {
                            TextButton(onClick = {
                                repoVm.setMode(if (ui.mode == SearchMode.RC) SearchMode.CHASSIS else SearchMode.RC)
                            }) { Text(if (ui.mode == SearchMode.RC) "RC" else "CH", fontWeight = FontWeight.Bold) }
                        },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        shape = RoundedCornerShape(8.dp),
                        textStyle = MaterialTheme.typography.bodyMedium.copy(
                            fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, letterSpacing = 3.sp),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface),
                        modifier = Modifier.fillMaxWidth()
                    )
                }
            }

            ui.searchError?.let {
                Surface(color = MaterialTheme.colorScheme.errorContainer, modifier = Modifier.fillMaxWidth()) {
                    Text(it, Modifier.padding(10.dp), style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }
            }

            when {
                ui.isSearching || ui.loadingRecord ->
                    Box(Modifier.fillMaxSize(), Alignment.Center) { CircularProgressIndicator() }
                hasResults -> LazyColumn(Modifier.fillMaxSize()) {
                    item {
                        Surface(color = MaterialTheme.colorScheme.primaryContainer, modifier = Modifier.fillMaxWidth()) {
                            Text("${ui.results.size} vehicle(s) found",
                                Modifier.padding(horizontal = 12.dp, vertical = 4.dp),
                                style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onPrimaryContainer)
                        }
                    }
                    items(ui.results, key = { it.id }) { item ->
                        val display = if (ui.mode == SearchMode.RC) item.vehicleNo else item.chassisNo
                        Row(
                            Modifier.fillMaxWidth()
                                .clickable {
                                    repoVm.selectVehicle(item.id, userId) {
                                        val dest = if (repoVm.ui.value.flowMode == com.vkenterprises.crmrs.viewmodel.RepoFlow.BILLING)
                                            Screen.BillingPreview.route else Screen.RepoPreview.route
                                        nav.navigate(dest)
                                    }
                                }
                                .padding(horizontal = 12.dp, vertical = 12.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(display.ifBlank { "—" }, fontWeight = FontWeight.Black,
                                modifier = Modifier.weight(1f))
                            Text(item.model.ifBlank { "—" }, style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                            Icon(Icons.Default.ChevronRight, null, tint = MaterialTheme.colorScheme.outline,
                                modifier = Modifier.size(16.dp))
                        }
                        HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.3f),
                            thickness = 0.5.dp)
                    }
                }
                else -> Spacer(Modifier.weight(1f))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RepoPreviewScreen(
    repoVm: RepoViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui by repoVm.ui.collectAsState()
    val userId by authVm.userId.collectAsState(initial = -1L)
    val agencyNameDefault by authVm.agencyName.collectAsState(initial = null)
    val record = ui.selectedRecord
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    val today = remember { SimpleDateFormat("dd/MM/yyyy", Locale.ENGLISH).format(Date()) }
    val todayLong = remember { SimpleDateFormat("dd MMM yyyy", Locale.ENGLISH).format(Date()) }
    val headOfficeName = ui.selectedHeadOffice?.name?.uppercase().orEmpty()

    var dateText by remember { mutableStateOf(today) }
    var policeStation by remember(ui.settings) { mutableStateOf(ui.settings?.policeStation ?: "") }
    var policeAddress by remember(ui.settings) { mutableStateOf(ui.settings?.policeAddress ?: "") }
    var loanAcNo by remember(record) { mutableStateOf(record?.agreementNo ?: "") }
    var vehicleRegNo by remember(record) { mutableStateOf(record?.vehicleNo ?: "") }
    var assets by remember(record) { mutableStateOf(record?.model ?: "") }
    var borrowerName by remember(record) { mutableStateOf(record?.customerName ?: "") }
    var residenceAddress by remember(record) { mutableStateOf(record?.customerAddress ?: "") }
    var agencyName by remember(ui.settings, agencyNameDefault) {
        mutableStateOf(ui.settings?.agencyName ?: agencyNameDefault ?: "")
    }
    var authorizedBy by remember(ui.settings, headOfficeName) {
        mutableStateOf(ui.settings?.authorizedBy ?: headOfficeName)
    }
    var authLetterDate by remember { mutableStateOf(todayLong) }
    var generating by remember { mutableStateOf(false) }
    var asDocx by remember { mutableStateOf(false) }

    val logoPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) scope.launch {
            val b64 = withContext(Dispatchers.IO) { compressImageToBase64(context, uri) }
            if (b64 != null) repoVm.uploadLogo(userId, b64)
        }
    }

    fun doGenerate(type: RepoDocType) {
        generating = true
        scope.launch {
            repoVm.saveSettings(userId, SaveRepoSettingsRequest(
                agencyName = agencyName.trim().ifBlank { null },
                authorizedBy = authorizedBy.trim().ifBlank { null },
                policeStation = policeStation.trim().ifBlank { null },
                policeAddress = policeAddress.trim().ifBlank { null }
            ))
            val logoUrl = repoVm.ui.value.settings?.logoUrl
            val result = withContext(Dispatchers.IO) {
                val logo = if (!logoUrl.isNullOrBlank()) RepoPdf.loadBitmap(logoUrl) else null
                val data = RepoPdf.LetterData(
                    docType = type,
                    dateText = dateText.trim(),
                    policeStation = policeStation.trim(),
                    policeAddress = policeAddress.trim(),
                    loanAcNo = loanAcNo.trim(),
                    vehicleRegNo = vehicleRegNo.trim(),
                    assets = assets.trim(),
                    borrowerName = borrowerName.trim(),
                    residenceAddress = residenceAddress.trim(),
                    agencyName = agencyName.trim(),
                    headOffice = authorizedBy.trim().ifBlank { headOfficeName },
                    authLetterDate = authLetterDate.trim(),
                    logo = logo
                )
                if (asDocx)
                    RepoDocx.generate(context, data) to
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                else
                    RepoPdf.generate(context, data) to "application/pdf"
            }
            generating = false
            RepoPdf.open(context, result.first, result.second)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Letter Details", fontWeight = FontWeight.Bold) },
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
                        FilterChip(selected = !asDocx, onClick = { asDocx = false },
                            label = { Text("PDF") },
                            leadingIcon = { Icon(Icons.Default.PictureAsPdf, null, Modifier.size(16.dp)) })
                        FilterChip(selected = asDocx, onClick = { asDocx = true },
                            label = { Text("DOCX") },
                            leadingIcon = { Icon(Icons.Default.Description, null, Modifier.size(16.dp)) })
                        if (generating) {
                            Spacer(Modifier.weight(1f))
                            CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                        }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Button(onClick = { doGenerate(RepoDocType.PRE) }, enabled = !generating,
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.weight(1f).height(48.dp)) {
                            Text("Generate Pre", fontWeight = FontWeight.Bold)
                        }
                        Button(onClick = { doGenerate(RepoDocType.POST) }, enabled = !generating,
                            shape = RoundedCornerShape(10.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF6A1B9A)),
                            modifier = Modifier.weight(1f).height(48.dp)) {
                            Text("Generate Post", fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }
        }
    ) { pad ->
        Column(
            Modifier.padding(pad).fillMaxSize().verticalScroll(rememberScrollState())
                .padding(horizontal = 14.dp, vertical = 10.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            SectionLabel("Head Office Logo")
            LogoRow(
                logoUrl = ui.settings?.logoUrl,
                uploading = ui.uploadingLogo,
                onPick = { logoPicker.launch("image/*") }
            )

            SectionLabel("Letter")
            RepoField("Date", dateText) { dateText = it }
            RepoField("Police Station", policeStation, "e.g. Miraj Rural Police Station") { policeStation = it }
            RepoField("Police Station Address", policeAddress,
                "e.g. Sangli Miraj Kupwad, Maharashtra 416410", singleLine = false) { policeAddress = it }

            SectionLabel("Vehicle & Borrower")
            RepoField("Loan A/c No.", loanAcNo) { loanAcNo = it }
            RepoField("Vehicle Registration No.", vehicleRegNo) { vehicleRegNo = it }
            RepoField("Assets Details", assets) { assets = it }
            RepoField("Name of Borrower", borrowerName) { borrowerName = it }
            RepoField("Residence Address", residenceAddress, singleLine = false) { residenceAddress = it }

            SectionLabel("Authorization")
            RepoField("Agency Name", agencyName, "Saved for this head office") { agencyName = it }
            RepoField("Authorized By (Head Office)", authorizedBy) { authorizedBy = it }
            RepoField("Authorization Letter Dated", authLetterDate) { authLetterDate = it }

            Spacer(Modifier.height(80.dp))
        }
    }
}

@Composable
private fun LogoRow(logoUrl: String?, uploading: Boolean, onPick: () -> Unit) {
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
                        model = logoUrl, contentDescription = "Logo",
                        modifier = Modifier.fillMaxSize().padding(6.dp))
                    else -> Icon(Icons.Default.Image, null, tint = MaterialTheme.colorScheme.outline)
                }
            }
        }
        Column(Modifier.weight(1f)) {
            Text(
                if (logoUrl.isNullOrBlank()) "No logo for this head office" else "Logo set — appears top-right of the letter",
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
private fun SectionLabel(text: String) {
    Text(text.uppercase(), style = MaterialTheme.typography.labelSmall,
        fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 6.dp))
}

@Composable
private fun RepoField(
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
