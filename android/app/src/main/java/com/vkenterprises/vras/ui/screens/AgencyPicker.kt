package com.vkenterprises.vras.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Business
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import com.vkenterprises.vras.data.models.AgencyListItem

@Composable
fun AgencyPickerField(
    agencies: List<AgencyListItem>,
    selected: AgencyListItem?,
    onSelect: (AgencyListItem) -> Unit,
    modifier: Modifier = Modifier
) {
    var open by remember { mutableStateOf(false) }

    OutlinedCard(
        onClick = { open = true },
        modifier = modifier.fillMaxWidth()
    ) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(Icons.Default.Business, null, tint = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    "Agency *",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Text(
                    selected?.name ?: "Tap to select your agency",
                    style = MaterialTheme.typography.bodyLarge,
                    fontWeight = if (selected != null) FontWeight.SemiBold else FontWeight.Normal,
                    color = if (selected != null) MaterialTheme.colorScheme.onSurface
                            else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Icon(Icons.Default.ArrowDropDown, null)
        }
    }

    if (open) {
        AgencyPickerDialog(
            agencies  = agencies,
            onDismiss = { open = false },
            onPick    = { onSelect(it); open = false }
        )
    }
}

@Composable
private fun AgencyPickerDialog(
    agencies: List<AgencyListItem>,
    onDismiss: () -> Unit,
    onPick: (AgencyListItem) -> Unit
) {
    var query by remember { mutableStateOf("") }
    val filtered = remember(query, agencies) {
        if (query.isBlank()) agencies
        else agencies.filter { it.name.contains(query.trim(), ignoreCase = true) }
    }

    Dialog(onDismissRequest = onDismiss) {
        Surface(
            shape = RoundedCornerShape(16.dp),
            color = MaterialTheme.colorScheme.surface,
            tonalElevation = 6.dp
        ) {
            Column(Modifier.padding(20.dp).fillMaxWidth()) {
                Text(
                    "Select your agency",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold
                )
                Spacer(Modifier.height(12.dp))
                OutlinedTextField(
                    value = query,
                    onValueChange = { query = it },
                    label = { Text("Search agency name") },
                    leadingIcon = { Icon(Icons.Default.Search, null) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(12.dp))
                when {
                    agencies.isEmpty() -> Text(
                        "No agencies available yet. Please try again in a moment.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    filtered.isEmpty() -> Text(
                        "No agency matches your search.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    else -> Column(
                        Modifier.heightIn(max = 320.dp).verticalScroll(rememberScrollState())
                    ) {
                        filtered.forEach { agency ->
                            Text(
                                agency.name,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable { onPick(agency) }
                                    .padding(vertical = 14.dp, horizontal = 4.dp),
                                style = MaterialTheme.typography.bodyLarge
                            )
                            HorizontalDivider()
                        }
                    }
                }
                Spacer(Modifier.height(8.dp))
                TextButton(onClick = onDismiss, modifier = Modifier.align(Alignment.End)) {
                    Text("Cancel")
                }
            }
        }
    }
}
