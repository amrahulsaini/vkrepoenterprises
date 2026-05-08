package com.vkenterprises.vras.ui.screens

import android.util.Base64
import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
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
                        Text("VK Enterprises", fontWeight = FontWeight.Bold)
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
                            if (bytes != null) {
                                AsyncImage(model = bytes, contentDescription = "Profile",
                                    contentScale = ContentScale.Crop,
                                    modifier = Modifier.size(32.dp).clip(CircleShape))
                            } else Icon(Icons.Default.AccountCircle, "Profile")
                        } else Icon(Icons.Default.AccountCircle, "Profile")
                    }
                    IconButton(onClick = {
                        authVm.logout()
                        nav.navigate(Screen.Login.route) { popUpTo(0) { inclusive = true } }
                    }) { Icon(Icons.Default.Logout, "Logout") }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface)
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {

            // ── Search bar ───────────────────────────────────────────────
            Surface(
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {

                    // RC / Chassis toggle
                    Row(
                        Modifier.fillMaxWidth()
                            .clip(RoundedCornerShape(10.dp))
                            .background(MaterialTheme.colorScheme.surface)
                    ) {
                        SearchModeTab("RC Number",  ui.mode == SearchMode.RC,
                            { searchVm.setMode(SearchMode.RC) },    Modifier.weight(1f))
                        SearchModeTab("Chassis No", ui.mode == SearchMode.CHASSIS,
                            { searchVm.setMode(SearchMode.CHASSIS) }, Modifier.weight(1f))
                    }

                    // Search field
                    OutlinedTextField(
                        value = ui.query,
                        onValueChange = { q -> searchVm.onQueryChange(q, userId) },
                        placeholder = {
                            Text(
                                if (ui.mode == SearchMode.RC) "Last 4 digits of RC number"
                                else "Last 5 digits of Chassis number",
                                style = MaterialTheme.typography.bodyMedium
                            )
                        },
                        leadingIcon = {
                            if (ui.isLoading)
                                CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp,
                                    color = MaterialTheme.colorScheme.primary)
                            else Icon(Icons.Default.Search, null)
                        },
                        trailingIcon = {
                            if (ui.query.isNotEmpty())
                                IconButton(onClick = { searchVm.onQueryChange("", userId) }) {
                                    Icon(Icons.Default.Close, "Clear")
                                }
                        },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(10.dp),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor   = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface
                        )
                    )
                }
            }

            // ── Error banner ─────────────────────────────────────────────
            if (ui.errorMsg != null) {
                Box(
                    Modifier.fillMaxWidth()
                        .background(MaterialTheme.colorScheme.errorContainer)
                        .padding(horizontal = 16.dp, vertical = 8.dp)
                ) {
                    Text(ui.errorMsg!!, style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }
            }

            // ── Results ──────────────────────────────────────────────────
            when {
                ui.results.isNotEmpty() -> {
                    // Header row
                    ResultHeader(ui.results.size)

                    LazyColumn(Modifier.fillMaxSize()) {
                        items(ui.results) { item ->
                            ResultRow(item) {
                                searchVm.selectResult(item)
                                nav.navigate(Screen.VehicleDetail.route)
                            }
                            HorizontalDivider(
                                color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.4f),
                                thickness = 0.5.dp
                            )
                        }
                    }
                }

                ui.query.isEmpty() -> {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Icon(Icons.Default.DirectionsCar, null,
                                Modifier.size(56.dp),
                                tint = MaterialTheme.colorScheme.outlineVariant)
                            Text("Enter last digits to search",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.outlineVariant)
                        }
                    }
                }

                !ui.isLoading -> {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Icon(Icons.Default.SearchOff, null,
                                Modifier.size(48.dp),
                                tint = MaterialTheme.colorScheme.outlineVariant)
                            Text("No results for \"${ui.query}\"",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ResultHeader(count: Int) {
    Row(
        Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.primaryContainer)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text("$count record(s) found",
            style = MaterialTheme.typography.labelMedium,
            fontWeight = FontWeight.SemiBold,
            color = MaterialTheme.colorScheme.onPrimaryContainer,
            modifier = Modifier.weight(1f))
        Text("Tap a row for details",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.7f))
    }
}

@Composable
private fun ResultRow(item: SearchResult, onClick: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        // Vehicle icon
        Surface(
            shape = RoundedCornerShape(8.dp),
            color = MaterialTheme.colorScheme.primaryContainer,
            modifier = Modifier.size(38.dp)
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(Icons.Default.DirectionsCar, null,
                    Modifier.size(20.dp),
                    tint = MaterialTheme.colorScheme.primary)
            }
        }

        // Main info
        Column(Modifier.weight(1f)) {
            Text(
                item.vehicleNo.ifBlank { item.chassisNo }.ifBlank { "—" },
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Bold,
                maxLines = 1, overflow = TextOverflow.Ellipsis
            )
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                if (item.customerName.isNotBlank())
                    Text(item.customerName,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1, overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f, fill = false))
            }
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                if (item.branchName.isNotBlank()) MiniChip(item.branchName)
                if (item.model.isNotBlank())      MiniChip(item.model)
                if (item.bucket.isNotBlank())     MiniChip(item.bucket)
            }
        }

        Icon(Icons.Default.ChevronRight, null,
            Modifier.size(18.dp),
            tint = MaterialTheme.colorScheme.outlineVariant)
    }
}

@Composable
private fun MiniChip(label: String) {
    Surface(
        shape = RoundedCornerShape(4.dp),
        color = MaterialTheme.colorScheme.secondaryContainer
    ) {
        Text(label,
            Modifier.padding(horizontal = 5.dp, vertical = 1.dp),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSecondaryContainer,
            maxLines = 1)
    }
}

@Composable
private fun SearchModeTab(
    label: String, selected: Boolean,
    onClick: () -> Unit, modifier: Modifier = Modifier
) {
    Box(
        modifier.clip(RoundedCornerShape(8.dp))
            .background(if (selected) MaterialTheme.colorScheme.primary
                        else MaterialTheme.colorScheme.surface)
            .clickable(onClick = onClick)
            .padding(vertical = 9.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(label,
            style = MaterialTheme.typography.labelMedium,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            color = if (selected) MaterialTheme.colorScheme.onPrimary
                    else MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
