package com.vkenterprises.vras.ui.screens

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthViewModel
import com.vkenterprises.vras.viewmodel.SearchViewModel
import com.vkenterprises.vras.viewmodel.SettingsViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    settingsVm: SettingsViewModel,
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui       by settingsVm.ui.collectAsState()
    val searchUi by searchVm.ui.collectAsState()
    val isAdmin  by authVm.isAdmin.collectAsState(initial = false)

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Settings", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        }
    ) { pad ->
        LazyColumn(
            modifier = Modifier.padding(pad).fillMaxSize(),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {

            // ── Role banner ──────────────────────────────────────────────
            item {
                val bgColor    = if (isAdmin) Color(0xFF1A237E) else MaterialTheme.colorScheme.primaryContainer
                val textColor  = if (isAdmin) Color.White else MaterialTheme.colorScheme.onPrimaryContainer
                val roleLabel  = if (isAdmin) "Administrator" else "Agent"
                val roleDesc   = if (isAdmin) "You are Admin" else "You are a user of VK Enterprises"
                val roleIcon   = if (isAdmin) Icons.Default.AdminPanelSettings else Icons.Default.Person
                Surface(
                    color  = bgColor,
                    shape  = RoundedCornerShape(12.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Row(
                        Modifier.padding(16.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(14.dp)
                    ) {
                        Icon(roleIcon, null, tint = textColor, modifier = Modifier.size(36.dp))
                        Column {
                            Text(roleDesc,
                                color = textColor,
                                fontWeight = FontWeight.Bold,
                                style = MaterialTheme.typography.titleSmall)
                            Text(roleLabel,
                                color = textColor.copy(alpha = 0.75f),
                                style = MaterialTheme.typography.bodySmall)
                        }
                    }
                }
            }

            // ── Admin tools ──────────────────────────────────────────────
            if (isAdmin) {
                item {
                    SectionCard(title = "Admin Tools") {
                        Button(
                            onClick = { nav.navigate(Screen.LiveUsers.route) },
                            modifier = Modifier.fillMaxWidth(),
                            colors = ButtonDefaults.buttonColors(
                                containerColor = Color(0xFF1A237E),
                                contentColor   = Color.White
                            )
                        ) {
                            Icon(Icons.Default.LocationOn, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(8.dp))
                            Text("View Live Users", fontWeight = FontWeight.SemiBold)
                        }
                    }
                }
            }

            // ── Server DB Stats (admin only) ─────────────────────────────
            if (isAdmin) {
                item {
                    SectionCard(title = "Server Database") {
                        if (ui.isLoading) {
                            Box(Modifier.fillMaxWidth().padding(16.dp), contentAlignment = Alignment.Center) {
                                CircularProgressIndicator(modifier = Modifier.size(24.dp))
                            }
                        } else if (ui.statsError != null) {
                            Row(Modifier.fillMaxWidth().padding(vertical = 8.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Icon(Icons.Default.CloudOff, null,
                                    tint = MaterialTheme.colorScheme.error,
                                    modifier = Modifier.size(16.dp))
                                Text(ui.statsError!!,
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.error)
                            }
                        } else {
                            StatRow("Vehicle Records", ui.serverVehicleRecords)
                            StatRow("RC Info Records", ui.serverRcRecords)
                            StatRow("Chassis Info Records", ui.serverChassisRecords)
                        }
                        Spacer(Modifier.height(4.dp))
                        TextButton(onClick = { settingsVm.loadAll() }) {
                            Icon(Icons.Default.Refresh, null, Modifier.size(16.dp))
                            Spacer(Modifier.width(4.dp))
                            Text("Refresh", style = MaterialTheme.typography.labelMedium)
                        }
                    }
                }
            }

            // ── Local Cache ──────────────────────────────────────────────
            item {
                SectionCard(title = "Local Cache") {
                    StatRow("Cached Records", ui.roomCount)
                }
            }

            // ── Sync ─────────────────────────────────────────────────────
            item {
                val infiniteTransition = rememberInfiniteTransition(label = "syncPulse")
                val pulseAlpha by infiniteTransition.animateFloat(
                    initialValue = 1f, targetValue = 0.35f,
                    animationSpec = infiniteRepeatable(
                        animation = tween(600, easing = FastOutSlowInEasing),
                        repeatMode = RepeatMode.Reverse
                    ), label = "pulseAlpha"
                )
                val syncBtnColor = when {
                    ui.isSyncing       -> MaterialTheme.colorScheme.primary
                    ui.syncHasUpdates  -> Color(0xFFD32F2F).copy(alpha = pulseAlpha)
                    ui.syncCompleted   -> Color(0xFF388E3C)
                    else               -> MaterialTheme.colorScheme.primary
                }
                val syncBtnLabel = when {
                    ui.isSyncing      -> "Syncing…"
                    ui.syncHasUpdates -> "New Updates Available — Tap to Sync"
                    ui.syncCompleted  -> "Up to Date"
                    else              -> "Sync Now (Check for Updates)"
                }
                val syncBtnIcon = when {
                    ui.syncCompleted && !ui.syncHasUpdates -> Icons.Default.CheckCircle
                    else -> Icons.Default.Sync
                }

                SectionCard(title = "Sync") {
                    if (ui.syncProgress != null) {
                        Text(
                            ui.syncProgress!!,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(bottom = 8.dp)
                        )
                    }
                    if (ui.isSyncing) {
                        LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                        Spacer(Modifier.height(8.dp))
                    }
                    Button(
                        onClick = { settingsVm.smartSync() },
                        enabled = !ui.isSyncing,
                        modifier = Modifier.fillMaxWidth(),
                        colors = ButtonDefaults.buttonColors(containerColor = syncBtnColor)
                    ) {
                        Icon(syncBtnIcon, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text(syncBtnLabel)
                    }
                    Spacer(Modifier.height(6.dp))
                    OutlinedButton(
                        onClick = { settingsVm.forceSync() },
                        enabled = !ui.isSyncing,
                        modifier = Modifier.fillMaxWidth(),
                        colors = ButtonDefaults.outlinedButtonColors(
                            contentColor = MaterialTheme.colorScheme.error
                        )
                    ) {
                        Icon(Icons.Default.DeleteSweep, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Force Full Re-download")
                    }
                }
            }

            // ── Display Preferences ──────────────────────────────────────
            item {
                SectionCard(title = "Display") {
                    Row(
                        Modifier.fillMaxWidth().padding(vertical = 4.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text("Two-Column Grid", style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.Medium)
                            Text("Show results in 2-column grid vs single list",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Switch(
                            checked = searchUi.twoColumnView,
                            onCheckedChange = { searchVm.setTwoColumnView(it) }
                        )
                    }
                    HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))
                    Row(
                        Modifier.fillMaxWidth().padding(vertical = 4.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text("View Only Online", style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.Medium)
                            Text("Skip local cache — search server directly for lowest latency",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Switch(
                            checked = searchUi.onlineOnly,
                            onCheckedChange = { searchVm.setOnlineOnly(it) }
                        )
                    }
                }
            }

            // ── Sync Logs (admin only) ────────────────────────────────────
            if (isAdmin) {
                item {
                    SectionCard(title = "Sync Logs (${ui.syncLogs.size} branches)") {
                        OutlinedButton(
                            onClick = { settingsVm.toggleLogs() },
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Icon(
                                if (ui.showLogs) Icons.Default.ExpandLess else Icons.Default.ExpandMore,
                                null, Modifier.size(18.dp)
                            )
                            Spacer(Modifier.width(6.dp))
                            Text(if (ui.showLogs) "Hide Logs" else "View All Logs")
                        }
                    }
                }

                if (ui.showLogs) {
                    if (ui.syncLogs.isEmpty()) {
                        item {
                            Text("No sync logs. Sync has not run yet.",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.padding(horizontal = 16.dp))
                        }
                    } else {
                        items(ui.syncLogs) { log ->
                            Surface(
                                color = MaterialTheme.colorScheme.surfaceVariant,
                                shape = MaterialTheme.shapes.small,
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                Row(
                                    Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Text(
                                        "Branch #${log.branchId}",
                                        style = MaterialTheme.typography.bodySmall,
                                        fontWeight = FontWeight.Medium
                                    )
                                    Text(
                                        log.uploadedAt,
                                        style = MaterialTheme.typography.labelSmall,
                                        fontFamily = FontFamily.Monospace,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                }
                            }
                        }
                    }
                }
            }

            // ── Account ───────────────────────────────────────────────────
            item {
                SectionCard(title = "Account") {
                    Button(
                        onClick = {
                            authVm.logout()
                            nav.navigate(Screen.Login.route) { popUpTo(0) { inclusive = true } }
                        },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = MaterialTheme.colorScheme.error
                        ),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Icon(Icons.Default.Logout, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Logout")
                    }
                }
            }

            item { Spacer(Modifier.height(16.dp)) }
        }
    }
}

@Composable
private fun SectionCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text(
                title,
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(bottom = 10.dp)
            )
            content()
        }
    }
}

@Composable
private fun StatRow(label: String, value: Long) {
    Row(
        Modifier.fillMaxWidth().padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(label, style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(
            value.formatStatCount(),
            style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.SemiBold,
            fontFamily = FontFamily.Monospace
        )
    }
}

private fun Long.formatStatCount(): String = when {
    this >= 1_000_000L -> "${this / 1_000_000}.${(this % 1_000_000L) / 100_000L}M"
    this >= 1_000L     -> "${this / 1_000}.${(this % 1_000L) / 100L}K"
    else               -> "$this"
}
