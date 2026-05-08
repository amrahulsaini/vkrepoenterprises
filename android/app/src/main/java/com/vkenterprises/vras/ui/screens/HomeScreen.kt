package com.vkenterprises.vras.ui.screens

import android.util.Base64
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.vras.data.models.SearchResult
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthViewModel
import com.vkenterprises.vras.viewmodel.SearchMode
import com.vkenterprises.vras.viewmodel.SearchViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui       by searchVm.ui.collectAsState()
    val userId   by authVm.userId.collectAsState(initial = -1L)
    val userName by authVm.userName.collectAsState(initial = "")

    LaunchedEffect(ui.subscriptionExpired) {
        if (ui.subscriptionExpired) {
            nav.navigate(Screen.SubscriptionExpired.route) {
                popUpTo(Screen.Home.route) { inclusive = true }
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("VK Enterprises", fontWeight = FontWeight.Bold, fontSize = 16.sp)
                        if (userName.isNotEmpty())
                            Text("Hello, $userName",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                },
                actions = {
                    val pfpB64 by authVm.pfpBase64.collectAsState(initial = null)
                    IconButton(onClick = { nav.navigate(Screen.Profile.route) }) {
                        if (!pfpB64.isNullOrBlank()) {
                            val bytes = remember(pfpB64) {
                                runCatching { Base64.decode(pfpB64, Base64.DEFAULT) }.getOrNull()
                            }
                            if (bytes != null)
                                AsyncImage(model = bytes, contentDescription = null,
                                    contentScale = ContentScale.Crop,
                                    modifier = Modifier.size(30.dp).clip(CircleShape))
                            else Icon(Icons.Default.AccountCircle, null)
                        } else Icon(Icons.Default.AccountCircle, null)
                    }
                    IconButton(onClick = {
                        authVm.logout()
                        nav.navigate(Screen.Login.route) { popUpTo(0) { inclusive = true } }
                    }) { Icon(Icons.Default.Logout, null) }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface)
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {

            // Sync progress banner
            if (ui.isSyncing) {
                val pct = if (ui.syncTotal > 0) (ui.syncCurrent.toFloat() / ui.syncTotal) else 0f
                Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                    Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp)) {
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            Text("Syncing vehicle data…",
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onPrimaryContainer)
                            Text("${ui.syncCurrent.toInt().formatCount()} / ${ui.syncTotal.toInt().formatCount()}",
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onPrimaryContainer)
                        }
                        Spacer(Modifier.height(4.dp))
                        LinearProgressIndicator(
                            progress = { pct },
                            modifier = Modifier.fillMaxWidth(),
                            color = MaterialTheme.colorScheme.primary,
                            trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.2f)
                        )
                    }
                }
            }

            // ── Search bar ───────────────────────────────────────────────
            Surface(
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    // Mode toggle
                    Row(
                        Modifier.fillMaxWidth()
                            .clip(RoundedCornerShape(8.dp))
                            .background(MaterialTheme.colorScheme.surface)
                    ) {
                        ModeTab("RC Number",  ui.mode == SearchMode.RC,
                            { searchVm.setMode(SearchMode.RC) }, Modifier.weight(1f))
                        ModeTab("Chassis No", ui.mode == SearchMode.CHASSIS,
                            { searchVm.setMode(SearchMode.CHASSIS) }, Modifier.weight(1f))
                    }

                    // Search input — capped at 4 (RC) or 5 (Chassis) digits, no spinner
                    val maxLen = if (ui.mode == SearchMode.RC) 4 else 5
                    OutlinedTextField(
                        value = ui.inputText,
                        onValueChange = { searchVm.onInputChange(it, userId) },
                        placeholder = {
                            Text(
                                if (ui.mode == SearchMode.RC) "Enter last 4 digits of RC"
                                else "Enter last 5 digits of Chassis",
                                style = MaterialTheme.typography.bodySmall
                            )
                        },
                        leadingIcon = { Icon(Icons.Default.Search, null, Modifier.size(18.dp)) },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(8.dp),
                        textStyle = MaterialTheme.typography.bodyMedium.copy(
                            fontFamily = FontFamily.Monospace,
                            letterSpacing = 3.sp
                        ),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor   = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface
                        )
                    )
                }
            }

            // ── Error ────────────────────────────────────────────────────
            if (ui.errorMsg != null) {
                Surface(color = MaterialTheme.colorScheme.errorContainer) {
                    Text(ui.errorMsg!!, Modifier.fillMaxWidth().padding(10.dp),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }
            }

            // ── Results ──────────────────────────────────────────────────
            if (ui.results.isNotEmpty()) {
                // Count bar
                Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                    Text(
                        "${ui.results.size} record(s) found",
                        Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 5.dp),
                        style = MaterialTheme.typography.labelSmall,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onPrimaryContainer
                    )
                }
                // 2-column grid
                LazyVerticalGrid(
                    columns = GridCells.Fixed(2),
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(0.dp)
                ) {
                    items(ui.results) { item ->
                        VehicleGridCell(item, ui.mode) {
                            searchVm.selectResult(item)
                            nav.navigate(Screen.VehicleDetail.route)
                        }
                    }
                }
            } else if (ui.errorMsg == null) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Icon(Icons.Default.DirectionsCar, null, Modifier.size(48.dp),
                            tint = MaterialTheme.colorScheme.outlineVariant)
                        val hint = if (ui.mode == SearchMode.RC) "Enter last 4 digits of RC"
                                   else "Enter last 5 digits of Chassis"
                        Text(hint, style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.outlineVariant)
                    }
                }
            }
        }
    }
}

@Composable
private fun VehicleGridCell(item: SearchResult, mode: SearchMode, onClick: () -> Unit) {
    val display = when (mode) {
        SearchMode.RC      -> item.vehicleNo.ifBlank { "—" }
        SearchMode.CHASSIS -> item.chassisNo.ifBlank { "—" }
    }
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(
            display,
            style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Medium,
            fontFamily = FontFamily.Monospace,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f)
        )
        Icon(Icons.Default.ChevronRight, null,
            Modifier.size(14.dp),
            tint = MaterialTheme.colorScheme.outlineVariant)
    }
    HorizontalDivider(
        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.3f),
        thickness = 0.5.dp
    )
}

@Composable
private fun ModeTab(label: String, selected: Boolean, onClick: () -> Unit, modifier: Modifier) {
    Box(
        modifier.clip(RoundedCornerShape(6.dp))
            .background(
                if (selected) MaterialTheme.colorScheme.primary
                else MaterialTheme.colorScheme.surface
            )
            .clickable(onClick = onClick)
            .padding(vertical = 8.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(label,
            style = MaterialTheme.typography.labelMedium,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            color = if (selected) MaterialTheme.colorScheme.onPrimary
                    else MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

private fun Int.formatCount(): String = when {
    this >= 1_000_000 -> "${this / 1_000_000}.${(this % 1_000_000) / 100_000}M"
    this >= 1_000     -> "${this / 1_000}K"
    else              -> "$this"
}
