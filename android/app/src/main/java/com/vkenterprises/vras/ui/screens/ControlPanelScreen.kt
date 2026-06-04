package com.vkenterprises.vras.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.vras.data.models.AdminUserItem
import com.vkenterprises.vras.viewmodel.ControlPanelViewModel
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ControlPanelScreen(
    vm: ControlPanelViewModel,
    userId: Long,
    nav: NavController
) {
    val ui by vm.ui.collectAsState()

    LaunchedEffect(userId) { if (userId > 0) vm.init(userId) }

    LaunchedEffect(ui.errorMsg) {
        // errors are shown inline; nothing to do here
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Control Panel", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = {
                        if (ui.selectedUser != null) vm.clearUser() else nav.popBackStack()
                    }) { Icon(Icons.Default.ArrowBack, null) }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface)
            )
        }
    ) { pad ->
        Box(Modifier.padding(pad).fillMaxSize()) {
            when {
                ui.passwordGate          -> PasswordGate(vm, ui)
                ui.selectedUser != null  -> UserDetail(vm, ui)
                else                     -> UserList(vm, ui)
            }
            ui.errorMsg?.let { msg ->
                Snackbar(
                    modifier = Modifier.align(Alignment.BottomCenter).padding(12.dp),
                    action = { TextButton(onClick = { vm.dismissError() }) { Text("OK") } }
                ) { Text(msg) }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PasswordGate(vm: ControlPanelViewModel, ui: com.vkenterprises.vras.viewmodel.ControlPanelUiState) {
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Icon(Icons.Default.Lock, null, Modifier.size(48.dp),
            tint = MaterialTheme.colorScheme.primary)
        Spacer(Modifier.height(12.dp))
        Text("Enter Control Panel Password", fontWeight = FontWeight.Bold,
            style = MaterialTheme.typography.titleMedium)
        Text("Set for you by your administrator in the desktop app.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.height(16.dp))
        OutlinedTextField(
            value = ui.passwordInput,
            onValueChange = { vm.onPasswordChange(it) },
            label = { Text("Password") },
            singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
            isError = ui.passwordError != null,
            modifier = Modifier.fillMaxWidth()
        )
        ui.passwordError?.let {
            Text(it, color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.labelSmall,
                modifier = Modifier.align(Alignment.Start).padding(top = 4.dp))
        }
        Spacer(Modifier.height(16.dp))
        Button(
            onClick = { vm.verifyPassword() },
            enabled = !ui.verifying,
            modifier = Modifier.fillMaxWidth()
        ) {
            if (ui.verifying) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
            else Text("Unlock", fontWeight = FontWeight.Bold)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun UserList(vm: ControlPanelViewModel, ui: com.vkenterprises.vras.viewmodel.ControlPanelUiState) {
    Column(Modifier.fillMaxSize()) {
        OutlinedTextField(
            value = ui.filterText,
            onValueChange = { vm.onFilterChange(it) },
            label = { Text("Search by name or mobile") },
            leadingIcon = { Icon(Icons.Default.Search, null) },
            singleLine = true,
            modifier = Modifier.fillMaxWidth().padding(12.dp)
        )
        if (ui.usersLoading) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
        } else {
            val q = ui.filterText.trim().lowercase()
            val filtered = if (q.isBlank()) ui.users
                else ui.users.filter {
                    it.name.lowercase().contains(q) || it.mobile.lowercase().contains(q)
                }
            LazyColumn(
                Modifier.fillMaxSize(),
                contentPadding = PaddingValues(horizontal = 12.dp),
                verticalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                items(filtered) { user ->
                    Card(
                        onClick = { vm.selectUser(user) },
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Row(
                            Modifier.padding(12.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(Modifier.weight(1f)) {
                                Text(user.name, fontWeight = FontWeight.Bold)
                                Text(user.mobile,
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            StatusDot("Active", user.isActive, Color(0xFF388E3C))
                            if (user.isStopped)     StatusDot("Stopped", true, Color(0xFFD32F2F))
                            if (user.isBlacklisted) StatusDot("Blocked", true, Color(0xFF6A1B9A))
                            Icon(Icons.Default.ChevronRight, null,
                                tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun StatusDot(label: String, on: Boolean, color: Color) {
    if (!on) return
    Surface(
        shape = RoundedCornerShape(4.dp),
        color = color.copy(alpha = 0.15f),
        modifier = Modifier.padding(end = 6.dp)
    ) {
        Text(label, style = MaterialTheme.typography.labelSmall,
            color = color, fontWeight = FontWeight.SemiBold,
            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun UserDetail(vm: ControlPanelViewModel, ui: com.vkenterprises.vras.viewmodel.ControlPanelUiState) {
    val user = ui.selectedUser ?: return
    val profile = ui.selectedProfile
    // Reset per-user so a stale preview/reject dialog never carries over.
    var previewUrl  by remember(user.id) { mutableStateOf<String?>(null) }
    var rejectDialog by remember(user.id) { mutableStateOf(false) }
    val onPreview: (String?) -> Unit = { url -> if (!url.isNullOrBlank()) previewUrl = url }
    val onRejectKyc: () -> Unit = { rejectDialog = true }
    LazyColumn(
        Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        // ── Header: name + mobile + pfp ─────────────────────────────────
        item {
            Card(shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth()) {
                Row(
                    Modifier.padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    val pfpUrl = profile?.pfpUrl
                    if (!pfpUrl.isNullOrBlank()) {
                        AsyncImage(
                            model = pfpUrl,
                            contentDescription = null,
                            contentScale = ContentScale.Crop,
                            modifier = Modifier
                                .size(56.dp)
                                .clip(RoundedCornerShape(28.dp))
                        )
                    } else {
                        Surface(
                            shape = RoundedCornerShape(28.dp),
                            color = MaterialTheme.colorScheme.primaryContainer,
                            modifier = Modifier.size(56.dp)
                        ) {
                            Box(contentAlignment = Alignment.Center) {
                                Text(
                                    user.name.take(1).uppercase(),
                                    fontWeight = FontWeight.Bold,
                                    style = MaterialTheme.typography.titleLarge,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer
                                )
                            }
                        }
                    }
                    Column(Modifier.weight(1f)) {
                        Text(user.name, fontWeight = FontWeight.Bold,
                            style = MaterialTheme.typography.titleMedium)
                        Text(user.mobile, style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            fontFamily = FontFamily.Monospace)
                        if (!user.address.isNullOrBlank())
                            Text(user.address, style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
        // ── Personal info ───────────────────────────────────────────────
        item {
            Card(shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    SectionTitle("Personal Info")
                    if (ui.profileLoading && profile == null) {
                        CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    } else {
                        InfoRow("Address",  profile?.address)
                        InfoRow("Pincode",  profile?.pincode)
                        InfoRow("Balance",  profile?.balance?.let { "₹${"%.2f".format(it)}" })
                        InfoRow("Joined",   profile?.createdAt)
                    }
                }
            }
        }
        // ── KYC verification (documents + OKYC details + review actions) ─
        item {
            Card(shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    val kyc = profile?.kyc
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        SectionTitle("KYC Verification", padBottom = false)
                        KycStatusBadge(kyc?.kycStatus)
                    }
                    if (ui.profileLoading && profile == null) {
                        CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    } else if (kyc?.kycSubmitted != true) {
                        Text("User has not uploaded KYC documents.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        // Rejection note (shown to the agent on next login).
                        if (kyc.kycStatus.equals("failed", true) && !kyc.rejectNote.isNullOrBlank()) {
                            Surface(
                                shape = RoundedCornerShape(6.dp),
                                color = MaterialTheme.colorScheme.errorContainer,
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                Text("Reject note: ${kyc.rejectNote}",
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.onErrorContainer,
                                    modifier = Modifier.padding(8.dp))
                            }
                        }
                        // Documents — tap any to preview full-screen.
                        Text("Documents (tap to preview)",
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            KycTile("Aadhaar Front", kyc.aadhaarFront, Modifier.weight(1f)) { onPreview(kyc.aadhaarFront) }
                            KycTile("Aadhaar Back",  kyc.aadhaarBack,  Modifier.weight(1f)) { onPreview(kyc.aadhaarBack) }
                            KycTile("PAN Front",     kyc.panFront,     Modifier.weight(1f)) { onPreview(kyc.panFront) }
                        }
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            KycTile("Selfie",      kyc.selfie,       Modifier.weight(1f)) { onPreview(kyc.selfie) }
                            KycTile("UIDAI Photo", kyc.aadhaarPhoto, Modifier.weight(1f)) { onPreview(kyc.aadhaarPhoto) }
                            Spacer(Modifier.weight(1f))
                        }
                        // Aadhaar OKYC demographics fetched at registration.
                        Spacer(Modifier.height(2.dp))
                        Text(
                            if (kyc.aadhaarVerified) "✓ Aadhaar verified with UIDAI (OKYC)"
                            else "Aadhaar not OTP-verified",
                            style = MaterialTheme.typography.labelSmall,
                            fontWeight = FontWeight.SemiBold,
                            color = if (kyc.aadhaarVerified) Color(0xFF16A34A)
                                    else MaterialTheme.colorScheme.onSurfaceVariant)
                        InfoRow("Aadhaar No.",
                            kyc.aadhaarNumber ?: kyc.aadhaarLast4?.let { "XXXX XXXX $it" }, mono = true)
                        InfoRow("Name",     kyc.aadhaarName)
                        InfoRow("DOB",      kyc.aadhaarDob)
                        InfoRow("Gender",   kyc.aadhaarGender)
                        InfoRow("Address",  kyc.aadhaarAddress)
                        InfoRow("Location",
                            if (kyc.lat != null && kyc.lng != null)
                                (kyc.locationLabel?.let { "$it  " } ?: "") +
                                    "(%.5f, %.5f)".format(kyc.lat, kyc.lng)
                            else null)

                        // ── Review actions (Mark Verified / Reject) ──────────
                        Spacer(Modifier.height(4.dp))
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp),
                            modifier = Modifier.fillMaxWidth()) {
                            Button(
                                onClick = { vm.setKycStatus("success", null) },
                                enabled = !ui.busy && !kyc.kycStatus.equals("success", true),
                                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF16A34A)),
                                modifier = Modifier.weight(1f)
                            ) {
                                Icon(Icons.Default.VerifiedUser, null, Modifier.size(16.dp))
                                Spacer(Modifier.width(4.dp))
                                Text("Mark Verified")
                            }
                            OutlinedButton(
                                onClick = onRejectKyc,
                                enabled = !ui.busy,
                                colors = ButtonDefaults.outlinedButtonColors(
                                    contentColor = MaterialTheme.colorScheme.error),
                                modifier = Modifier.weight(1f)
                            ) {
                                Icon(Icons.Default.Block, null, Modifier.size(16.dp))
                                Spacer(Modifier.width(4.dp))
                                Text("Reject")
                            }
                        }
                    }
                }
            }
        }
        // ── Status toggles ──────────────────────────────────────────────
        item {
            Card(shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    SectionTitle("User Status")
                    ToggleRow("Active account", user.isActive, ui.busy) { vm.setActive(it) }
                    // Promote/demote — ON = Admin, OFF = normal user.
                    ToggleRow("Admin (full access)", user.isAdmin, ui.busy) { vm.setAdmin(it) }
                    ToggleRow("App stopped", user.isStopped, ui.busy) { vm.setStopped(it) }
                    ToggleRow("Blacklisted", user.isBlacklisted, ui.busy) { vm.setBlacklisted(it) }
                }
            }
        }
        // ── Subscription / plan ─────────────────────────────────────────
        item {
            Card(shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp)) {
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        SectionTitle("Subscription Plan", padBottom = false)
                        if (ui.subs.isEmpty() && !ui.subsLoading)
                            TextButton(onClick = { vm.showAddDialog() }) {
                                Icon(Icons.Default.Add, null, Modifier.size(16.dp))
                                Text("Add Plan")
                            }
                    }
                    when {
                        ui.subsLoading -> CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                        ui.subs.isEmpty() -> Text("No active plan.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                        else -> ui.subs.forEach { s ->
                            Row(
                                Modifier.fillMaxWidth().padding(vertical = 4.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column(Modifier.weight(1f)) {
                                    Text("${s.startDate}  →  ${s.endDate}",
                                        fontWeight = FontWeight.SemiBold,
                                        style = MaterialTheme.typography.bodySmall)
                                    Text("₹${s.amount}" + (s.notes?.let { "  •  $it" } ?: ""),
                                        style = MaterialTheme.typography.labelSmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                                IconButton(onClick = { vm.deleteSubscription(s.id) }) {
                                    Icon(Icons.Default.Delete, null,
                                        tint = MaterialTheme.colorScheme.error)
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    if (ui.showAddDialog) AddPlanDialog(vm, ui)
    previewUrl?.let { url -> ImagePreviewDialog(url) { previewUrl = null } }
    if (rejectDialog) RejectKycDialog(
        userName  = user.name,
        busy      = ui.busy,
        onConfirm = { note -> rejectDialog = false; vm.setKycStatus("failed", note) },
        onDismiss = { rejectDialog = false }
    )
}

@Composable
private fun SectionTitle(label: String, padBottom: Boolean = true) {
    Text(
        label,
        fontWeight = FontWeight.Bold,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
        modifier = if (padBottom) Modifier.padding(bottom = 4.dp) else Modifier
    )
}

@Composable
private fun InfoRow(label: String, value: String?, mono: Boolean = false) {
    Row(
        Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            fontFamily = FontFamily.SansSerif,
            modifier = Modifier.weight(0.4f))
        Text(
            value?.takeIf { it.isNotBlank() } ?: "—",
            style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Bold,
            fontFamily = if (mono) FontFamily.Monospace else FontFamily.SansSerif,
            modifier = Modifier.weight(0.6f)
        )
    }
}

@Composable
private fun KycTile(
    label: String,
    url: String?,
    modifier: Modifier = Modifier,
    onClick: () -> Unit = {}
) {
    Column(
        modifier
            .clip(RoundedCornerShape(8.dp)),
        verticalArrangement = Arrangement.spacedBy(4.dp)
    ) {
        Text(label,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        if (url.isNullOrBlank()) {
            Surface(
                shape = RoundedCornerShape(6.dp),
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth().height(80.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text("Not uploaded",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        } else {
            AsyncImage(
                model = url, contentDescription = label,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxWidth().height(80.dp)
                    .clip(RoundedCornerShape(6.dp))
                    .clickable { onClick() }
            )
        }
    }
}

// Coloured KYC status pill — PENDING REVIEW / VERIFIED / REJECTED.
@Composable
private fun KycStatusBadge(status: String?) {
    val (label, bg, fg) = when (status?.lowercase()) {
        "success" -> Triple("VERIFIED",       Color(0xFFDCFCE7), Color(0xFF15803D))
        "failed"  -> Triple("REJECTED",       Color(0xFFFEE2E2), Color(0xFFB91C1C))
        "pending" -> Triple("PENDING REVIEW", Color(0xFFFEF9C3), Color(0xFF854D0E))
        else      -> Triple("—",              MaterialTheme.colorScheme.surfaceVariant,
                                              MaterialTheme.colorScheme.onSurfaceVariant)
    }
    Surface(shape = RoundedCornerShape(6.dp), color = bg) {
        Text("KYC: $label",
            style = MaterialTheme.typography.labelSmall,
            fontWeight = FontWeight.Bold,
            color = fg,
            modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp))
    }
}

// Full-screen, pinch-to-zoom KYC document preview. Tap outside / back to close.
@Composable
private fun ImagePreviewDialog(url: String, onDismiss: () -> Unit) {
    androidx.compose.ui.window.Dialog(
        onDismissRequest = onDismiss,
        properties = androidx.compose.ui.window.DialogProperties(usePlatformDefaultWidth = false)
    ) {
        var scale by remember { mutableStateOf(1f) }
        var offsetX by remember { mutableStateOf(0f) }
        var offsetY by remember { mutableStateOf(0f) }
        val tState = rememberTransformableState { zoom, pan, _ ->
            scale = (scale * zoom).coerceIn(1f, 5f)
            offsetX += pan.x; offsetY += pan.y
        }
        Box(
            Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.92f))
                .clickable { onDismiss() },
            contentAlignment = Alignment.Center
        ) {
            AsyncImage(
                model = url,
                contentDescription = "KYC document",
                contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxSize().padding(12.dp)
                    .graphicsLayer(
                        scaleX = scale, scaleY = scale,
                        translationX = offsetX, translationY = offsetY)
                    .transformable(tState)
            )
            Surface(
                shape = RoundedCornerShape(50),
                color = Color.White.copy(alpha = 0.15f),
                modifier = Modifier.align(Alignment.TopEnd).padding(16.dp)
            ) {
                IconButton(onClick = onDismiss) {
                    Icon(Icons.Default.Close, "Close", tint = Color.White)
                }
            }
        }
    }
}

// Reject dialog with an optional note the agent sees on next login.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun RejectKycDialog(
    userName: String,
    busy: Boolean,
    onConfirm: (String) -> Unit,
    onDismiss: () -> Unit
) {
    var note by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Reject KYC", fontWeight = FontWeight.Bold) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Reject $userName's KYC? They'll be asked to re-submit and the account is deactivated.",
                    style = MaterialTheme.typography.bodySmall)
                OutlinedTextField(
                    value = note, onValueChange = { note = it },
                    label = { Text("Reason (optional)") },
                    modifier = Modifier.fillMaxWidth())
            }
        },
        confirmButton = {
            Button(
                onClick = { onConfirm(note) },
                enabled = !busy,
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
            ) { Text("Reject") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } }
    )
}

@Composable
private fun ToggleRow(label: String, checked: Boolean, busy: Boolean, onChange: (Boolean) -> Unit) {
    Row(
        Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, style = MaterialTheme.typography.bodyMedium)
        Switch(checked = checked, enabled = !busy, onCheckedChange = onChange)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun AddPlanDialog(vm: ControlPanelViewModel, ui: com.vkenterprises.vras.viewmodel.ControlPanelUiState) {
    var pickingStart by remember { mutableStateOf(false) }
    var pickingEnd   by remember { mutableStateOf(false) }

    if (pickingStart) {
        DateField.PickDialog(
            initial = ui.addStartDate,
            title   = "Pick start date",
            onPick  = { picked -> vm.onAddStartDate(picked); pickingStart = false },
            onCancel = { pickingStart = false }
        )
    }
    if (pickingEnd) {
        DateField.PickDialog(
            initial = ui.addEndDate,
            title   = "Pick end date",
            onPick  = { picked -> vm.onAddEndDate(picked); pickingEnd = false },
            onCancel = { pickingEnd = false }
        )
    }

    AlertDialog(
        onDismissRequest = { vm.hideAddDialog() },
        title = { Text("Add Plan", fontWeight = FontWeight.Bold) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                // Read-only fields with a calendar trailing icon — tap anywhere
                // on the field to open the Material 3 date picker.
                OutlinedTextField(
                    value = ui.addStartDate,
                    onValueChange = {},
                    label = { Text("Start date") },
                    readOnly = true,
                    singleLine = true,
                    trailingIcon = {
                        IconButton(onClick = { pickingStart = true }) {
                            Icon(Icons.Default.CalendarMonth, null)
                        }
                    },
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable { pickingStart = true })
                OutlinedTextField(
                    value = ui.addEndDate,
                    onValueChange = {},
                    label = { Text("End date") },
                    readOnly = true,
                    singleLine = true,
                    trailingIcon = {
                        IconButton(onClick = { pickingEnd = true }) {
                            Icon(Icons.Default.CalendarMonth, null)
                        }
                    },
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable { pickingEnd = true })
                OutlinedTextField(
                    value = ui.addAmount, onValueChange = { vm.onAddAmount(it) },
                    label = { Text("Amount (₹)") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth())
                OutlinedTextField(
                    value = ui.addNotes, onValueChange = { vm.onAddNotes(it) },
                    label = { Text("Notes (optional)") }, singleLine = true,
                    modifier = Modifier.fillMaxWidth())
                ui.addError?.let {
                    Text(it, color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.labelSmall)
                }
            }
        },
        confirmButton = {
            Button(onClick = { vm.addSubscription() }, enabled = !ui.adding) {
                if (ui.adding) CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                else Text("Save")
            }
        },
        dismissButton = { TextButton(onClick = { vm.hideAddDialog() }) { Text("Cancel") } }
    )
}

// Material 3 DatePickerDialog wrapper — accepts a YYYY-MM-DD seed and returns
// a YYYY-MM-DD pick. Kept in a small object so the AddPlanDialog stays tidy.
private object DateField {
    private val Fmt: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd")

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    fun PickDialog(
        initial: String,
        title: String,
        onPick: (String) -> Unit,
        onCancel: () -> Unit
    ) {
        val seedMillis = remember(initial) {
            runCatching {
                if (initial.isNotBlank())
                    java.time.LocalDate.parse(initial, Fmt)
                        .atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
                else null
            }.getOrNull()
        }
        val state = rememberDatePickerState(initialSelectedDateMillis = seedMillis)
        DatePickerDialog(
            onDismissRequest = onCancel,
            confirmButton = {
                TextButton(onClick = {
                    val ms = state.selectedDateMillis
                    if (ms != null) {
                        val date = Instant.ofEpochMilli(ms)
                            .atZone(ZoneOffset.UTC).toLocalDate()
                        onPick(date.format(Fmt))
                    } else {
                        onCancel()
                    }
                }) { Text("OK") }
            },
            dismissButton = {
                TextButton(onClick = onCancel) { Text("Cancel") }
            }
        ) {
            DatePicker(
                state = state,
                title = { Text(title, modifier = Modifier.padding(16.dp),
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.Bold) }
            )
        }
    }
}
