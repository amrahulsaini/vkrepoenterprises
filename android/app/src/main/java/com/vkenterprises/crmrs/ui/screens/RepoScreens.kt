package com.vkenterprises.crmrs.ui.screens

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
import com.vkenterprises.crmrs.utils.RepoPdf
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.RepoViewModel
import com.vkenterprises.crmrs.viewmodel.SearchMode
import kotlinx.coroutines.launch
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
                subtitle = "Coming soon",
                icon = Icons.Default.AssignmentTurnedIn,
                accent = MaterialTheme.colorScheme.outline,
                enabled = false
            ) { }
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
                                        nav.navigate(Screen.RepoPreview.route)
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
                Button(
                    onClick = {
                        generating = true
                        scope.launch {
                            repoVm.saveSettings(userId, SaveRepoSettingsRequest(
                                agencyName = agencyName.trim().ifBlank { null },
                                authorizedBy = authorizedBy.trim().ifBlank { null },
                                policeStation = policeStation.trim().ifBlank { null },
                                policeAddress = policeAddress.trim().ifBlank { null }
                            ))
                            val file = RepoPdf.generate(context, RepoPdf.LetterData(
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
                                authLetterDate = authLetterDate.trim()
                            ))
                            generating = false
                            RepoPdf.open(context, file)
                        }
                    },
                    enabled = !generating,
                    shape = RoundedCornerShape(10.dp),
                    modifier = Modifier.fillMaxWidth().navigationBarsPadding().padding(12.dp).height(50.dp)
                ) {
                    if (generating) {
                        CircularProgressIndicator(Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary,
                            strokeWidth = 2.dp)
                    } else {
                        Icon(Icons.Default.PictureAsPdf, null); Spacer(Modifier.width(8.dp))
                        Text("Generate PDF", fontWeight = FontWeight.Bold)
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

            Spacer(Modifier.height(70.dp))
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
