package com.vkenterprises.vras.ui.screens

import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
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

    // Navigate to subscription expired if expired
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
                            Text(
                                "Hello, $userName",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                    }
                },
                actions = {
                    IconButton(onClick = {
                        authVm.logout()
                        nav.navigate(Screen.Login.route) {
                            popUpTo(0) { inclusive = true }
                        }
                    }) {
                        Icon(Icons.Default.Logout, "Logout")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        }
    ) { pad ->
        Column(
            Modifier
                .padding(pad)
                .fillMaxSize()
        ) {
            // Mode selector + search bar
            Surface(
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    // RC / Chassis toggle
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(12.dp))
                            .background(MaterialTheme.colorScheme.surface),
                        horizontalArrangement = Arrangement.SpaceEvenly
                    ) {
                        SearchModeTab(
                            label = "RC Number",
                            selected = ui.mode == SearchMode.RC,
                            onClick = { searchVm.setMode(SearchMode.RC) },
                            modifier = Modifier.weight(1f)
                        )
                        SearchModeTab(
                            label = "Chassis No",
                            selected = ui.mode == SearchMode.CHASSIS,
                            onClick = { searchVm.setMode(SearchMode.CHASSIS) },
                            modifier = Modifier.weight(1f)
                        )
                    }

                    // Search field
                    val hint = if (ui.mode == SearchMode.RC)
                        "Enter last 4 digits of RC…"
                    else
                        "Enter last 5 digits of Chassis…"

                    OutlinedTextField(
                        value = ui.query,
                        onValueChange = { q ->
                            searchVm.onQueryChange(q.uppercase(), userId)
                        },
                        placeholder = { Text(hint, style = MaterialTheme.typography.bodyMedium) },
                        leadingIcon = {
                            if (ui.isLoading)
                                CircularProgressIndicator(
                                    Modifier.size(20.dp),
                                    strokeWidth = 2.dp,
                                    color = MaterialTheme.colorScheme.primary
                                )
                            else
                                Icon(Icons.Default.Search, null)
                        },
                        trailingIcon = {
                            if (ui.query.isNotEmpty()) {
                                IconButton(onClick = { searchVm.onQueryChange("", userId) }) {
                                    Icon(Icons.Default.Close, "Clear")
                                }
                            }
                        },
                        keyboardOptions = KeyboardOptions(
                            keyboardType = KeyboardType.Text,
                            capitalization = KeyboardCapitalization.Characters
                        ),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface
                        )
                    )
                }
            }

            // Results hint / error
            AnimatedVisibility(
                visible = ui.hint.isNotEmpty() || ui.errorMsg != null,
                enter = fadeIn() + slideInVertically(),
                exit = fadeOut()
            ) {
                Box(
                    Modifier
                        .fillMaxWidth()
                        .background(
                            if (ui.errorMsg != null)
                                MaterialTheme.colorScheme.errorContainer
                            else
                                MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.5f)
                        )
                        .padding(horizontal = 16.dp, vertical = 8.dp)
                ) {
                    Text(
                        ui.errorMsg ?: ui.hint,
                        style = MaterialTheme.typography.bodySmall,
                        color = if (ui.errorMsg != null)
                            MaterialTheme.colorScheme.onErrorContainer
                        else
                            MaterialTheme.colorScheme.onSurface
                    )
                }
            }

            // Results list
            if (ui.results.isEmpty() && !ui.isLoading && ui.query.isEmpty()) {
                // Empty state
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Icon(
                            Icons.Default.DirectionsCar,
                            null,
                            Modifier.size(64.dp),
                            tint = MaterialTheme.colorScheme.outlineVariant
                        )
                        Text(
                            "Search by RC or Chassis number",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.outlineVariant
                        )
                        Text(
                            "Type 4+ digits to see instant results",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.outlineVariant
                        )
                    }
                }
            } else if (ui.results.isEmpty() && !ui.isLoading && ui.query.isNotEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Icon(
                            Icons.Default.SearchOff,
                            null,
                            Modifier.size(56.dp),
                            tint = MaterialTheme.colorScheme.outlineVariant
                        )
                        Text(
                            "No results for \"${ui.query}\"",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            } else {
                LazyColumn(
                    Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    itemsIndexed(ui.results) { idx, item ->
                        VehicleCard(
                            item = item,
                            onClick = { nav.navigate(Screen.VehicleDetail.go(idx)) }
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun SearchModeTab(
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    Box(
        modifier
            .clip(RoundedCornerShape(10.dp))
            .background(
                if (selected) MaterialTheme.colorScheme.primary
                else MaterialTheme.colorScheme.surface
            )
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(
            label,
            style = MaterialTheme.typography.labelMedium,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            color = if (selected)
                MaterialTheme.colorScheme.onPrimary
            else
                MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

@Composable
private fun VehicleCard(item: SearchResult, onClick: () -> Unit) {
    Card(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surface
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Row(
            Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // Icon badge
            Surface(
                shape = RoundedCornerShape(10.dp),
                color = MaterialTheme.colorScheme.primaryContainer,
                modifier = Modifier.size(44.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        Icons.Default.DirectionsCar,
                        null,
                        Modifier.size(24.dp),
                        tint = MaterialTheme.colorScheme.primary
                    )
                }
            }

            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(
                    item.vehicleNo.ifBlank { "—" },
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.Bold
                )
                if (item.customerName.isNotBlank()) {
                    Text(
                        item.customerName,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1
                    )
                }
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    if (item.branchName.isNotBlank()) {
                        InfoChip(item.branchName)
                    }
                    if (item.model.isNotBlank()) {
                        InfoChip(item.model)
                    }
                }
            }

            Icon(
                Icons.Default.ChevronRight,
                null,
                tint = MaterialTheme.colorScheme.outlineVariant
            )
        }
    }
}

@Composable
private fun InfoChip(label: String) {
    Surface(
        shape = RoundedCornerShape(4.dp),
        color = MaterialTheme.colorScheme.secondaryContainer
    ) {
        Text(
            label,
            Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSecondaryContainer,
            maxLines = 1
        )
    }
}
