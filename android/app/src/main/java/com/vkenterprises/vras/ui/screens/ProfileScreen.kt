package com.vkenterprises.vras.ui.screens

import android.net.Uri
import android.util.Base64
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.vras.data.models.ProfileResponse
import com.vkenterprises.vras.data.models.SubscriptionRecord
import com.vkenterprises.vras.viewmodel.ProfileUiState
import com.vkenterprises.vras.viewmodel.ProfileViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProfileScreen(
    vm: ProfileViewModel,
    userId: Long,
    nav: NavController
) {
    val context = LocalContext.current
    val state   by vm.state.collectAsState()
    val pfpUpdating by vm.pfpUpdating.collectAsState()

    LaunchedEffect(userId) { vm.load(userId) }

    val pfpPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        uri?.let {
            val bytes = context.contentResolver.openInputStream(it)?.readBytes()
            val b64   = bytes?.let { b -> Base64.encodeToString(b, Base64.NO_WRAP) }
            vm.updatePfp(userId, b64)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("My Profile") },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, null)
                    }
                },
                actions = {
                    if (pfpUpdating)
                        CircularProgressIndicator(Modifier.size(20.dp).padding(end = 8.dp), strokeWidth = 2.dp)
                }
            )
        }
    ) { pad ->
        when (val s = state) {
            is ProfileUiState.Loading -> Box(
                Modifier.fillMaxSize().padding(pad),
                contentAlignment = Alignment.Center
            ) { CircularProgressIndicator() }

            is ProfileUiState.Error -> Box(
                Modifier.fillMaxSize().padding(pad),
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text(s.message, color = MaterialTheme.colorScheme.error)
                    Button(onClick = { vm.load(userId) }) { Text("Retry") }
                }
            }

            is ProfileUiState.Success -> ProfileContent(
                profile = s.profile,
                pad     = pad,
                onChangePfp = { pfpPicker.launch("image/*") }
            )
        }
    }
}

@Composable
private fun ProfileContent(
    profile: ProfileResponse,
    pad: PaddingValues,
    onChangePfp: () -> Unit
) {
    Column(
        Modifier
            .padding(pad)
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
    ) {
        // ── Header ────────────────────────────────────────────────────────
        Surface(
            color = MaterialTheme.colorScheme.primaryContainer,
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(
                Modifier.padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Box(contentAlignment = Alignment.BottomEnd) {
                    if (!profile.pfpBase64.isNullOrBlank()) {
                        val bytes = remember(profile.pfpBase64) {
                            runCatching { Base64.decode(profile.pfpBase64, Base64.DEFAULT) }.getOrNull()
                        }
                        AsyncImage(
                            model = bytes, contentDescription = null,
                            contentScale = ContentScale.Crop,
                            modifier = Modifier.size(96.dp).clip(CircleShape)
                                .border(3.dp, MaterialTheme.colorScheme.primary, CircleShape)
                        )
                    } else {
                        Box(
                            Modifier.size(96.dp).clip(CircleShape)
                                .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.2f)),
                            contentAlignment = Alignment.Center
                        ) {
                            Text(
                                profile.name.firstOrNull()?.uppercase() ?: "?",
                                style = MaterialTheme.typography.headlineLarge,
                                color = MaterialTheme.colorScheme.primary
                            )
                        }
                    }
                    SmallFloatingActionButton(
                        onClick = onChangePfp,
                        containerColor = MaterialTheme.colorScheme.primary,
                        contentColor   = MaterialTheme.colorScheme.onPrimary,
                        modifier = Modifier.size(28.dp)
                    ) { Icon(Icons.Default.CameraAlt, null, Modifier.size(14.dp)) }
                }
                Text(profile.name, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onPrimaryContainer)
                Text(profile.mobile, style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.75f))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (profile.isAdmin)
                        AssistChip(onClick = {}, label = { Text("Admin") },
                            leadingIcon = { Icon(Icons.Default.AdminPanelSettings, null, Modifier.size(16.dp)) })
                    if (profile.isActive)
                        AssistChip(onClick = {}, label = { Text("Active") },
                            leadingIcon = { Icon(Icons.Default.CheckCircle, null, Modifier.size(16.dp)) })
                    else
                        AssistChip(onClick = {}, label = { Text("Inactive") },
                            colors = AssistChipDefaults.assistChipColors(
                                containerColor = MaterialTheme.colorScheme.errorContainer))
                }
            }
        }

        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {

            // ── Personal info ─────────────────────────────────────────────
            ProfileSection(title = "Personal Information") {
                InfoRow(Icons.Default.LocationOn, "Address", profile.address ?: "—")
                InfoRow(Icons.Default.PinDrop, "Pincode", profile.pincode ?: "—")
                InfoRow(Icons.Default.CalendarToday, "Joined", profile.createdAt)
                InfoRow(Icons.Default.AccountBalance, "Balance", "₹${profile.balance}")
            }

            // ── Bank details ──────────────────────────────────────────────
            ProfileSection(title = "Bank Details") {
                InfoRow(Icons.Default.AccountBalance, "Account Number",
                    if (profile.accountNumber.isNullOrBlank()) "Not provided" else profile.accountNumber)
                InfoRow(Icons.Default.Code, "IFSC Code",
                    if (profile.ifscCode.isNullOrBlank()) "Not provided" else profile.ifscCode)
            }

            // ── KYC ───────────────────────────────────────────────────────
            ProfileSection(title = "KYC Documents") {
                val kyc = profile.kyc
                if (kyc.kycSubmitted) {
                    Row(Modifier.padding(vertical = 4.dp),
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                        verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Default.VerifiedUser, null,
                            Modifier.size(18.dp), tint = Color(0xFF2E7D32))
                        Text("KYC Submitted", color = Color(0xFF2E7D32),
                            style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium)
                    }
                    Spacer(Modifier.height(8.dp))
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        KycDocThumb("Aadhaar Front", kyc.aadhaarFront, Modifier.weight(1f))
                        KycDocThumb("Aadhaar Back",  kyc.aadhaarBack,  Modifier.weight(1f))
                        KycDocThumb("PAN Front",     kyc.panFront,     Modifier.weight(1f))
                    }
                } else {
                    Row(verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        Icon(Icons.Default.Warning, null,
                            Modifier.size(18.dp), tint = MaterialTheme.colorScheme.error)
                        Text("KYC not submitted", color = MaterialTheme.colorScheme.error,
                            style = MaterialTheme.typography.bodyMedium)
                    }
                }
            }

            // ── Subscriptions ─────────────────────────────────────────────
            ProfileSection(title = "Subscriptions (${profile.subscriptions.size})") {
                if (profile.subscriptions.isEmpty()) {
                    Text("No subscription records.", style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                } else {
                    profile.subscriptions.forEach { sub ->
                        SubRow(sub)
                        if (sub != profile.subscriptions.last())
                            HorizontalDivider(Modifier.padding(vertical = 4.dp))
                    }
                }
            }

            Spacer(Modifier.height(16.dp))
        }
    }
}

@Composable
private fun ProfileSection(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary)
            HorizontalDivider(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.2f))
            content()
        }
    }
}

@Composable
private fun InfoRow(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, value: String) {
    Row(verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Icon(icon, null, Modifier.size(18.dp), tint = MaterialTheme.colorScheme.primary)
        Column {
            Text(label, style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(value, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

@Composable
private fun KycDocThumb(label: String, base64: String?, modifier: Modifier = Modifier) {
    OutlinedCard(modifier = modifier.height(80.dp)) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            if (!base64.isNullOrBlank()) {
                val bytes = remember(base64) {
                    runCatching { Base64.decode(base64, Base64.DEFAULT) }.getOrNull()
                }
                if (bytes != null) {
                    AsyncImage(
                        model = bytes, contentDescription = label,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize()
                    )
                    Box(
                        Modifier.fillMaxWidth().align(Alignment.BottomCenter)
                            .background(Color.Black.copy(alpha = 0.5f))
                            .padding(2.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(label, style = MaterialTheme.typography.labelSmall, color = Color.White)
                    }
                }
            } else {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(Icons.Default.HideImage, null,
                        Modifier.size(20.dp), tint = MaterialTheme.colorScheme.outlineVariant)
                    Text(label, style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.outlineVariant,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                        modifier = Modifier.padding(top = 2.dp))
                }
            }
        }
    }
}

@Composable
private fun SubRow(sub: SubscriptionRecord) {
    Row(
        Modifier.fillMaxWidth().padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Box(
                    Modifier.size(8.dp).clip(CircleShape)
                        .background(if (sub.isActive) Color(0xFF2E7D32) else MaterialTheme.colorScheme.outline)
                )
                Text("${sub.startDate}  →  ${sub.endDate}",
                    style = MaterialTheme.typography.bodySmall)
            }
            if (!sub.notes.isNullOrBlank())
                Text(sub.notes, style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Text("₹${sub.amount}", style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.SemiBold,
            color = MaterialTheme.colorScheme.primary)
    }
}
