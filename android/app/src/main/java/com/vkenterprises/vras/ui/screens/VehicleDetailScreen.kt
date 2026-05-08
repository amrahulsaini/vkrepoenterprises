package com.vkenterprises.vras.ui.screens

import androidx.compose.animation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import com.vkenterprises.vras.data.models.SearchResult
import com.vkenterprises.vras.viewmodel.SearchViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun VehicleDetailScreen(
    searchVm: SearchViewModel,
    index: Int,
    onBack: () -> Unit
) {
    val ui by searchVm.ui.collectAsState()
    val item = ui.results.getOrNull(index)

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(item?.vehicleNo ?: "Vehicle Detail", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.Default.ArrowBack, null)
                    }
                }
            )
        }
    ) { pad ->
        if (item == null) {
            Box(
                Modifier.fillMaxSize().padding(pad),
                contentAlignment = Alignment.Center
            ) {
                Text("Vehicle not found", color = MaterialTheme.colorScheme.onSurfaceVariant)
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
            // Header card
            Card(
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.primaryContainer
                ),
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    Modifier.padding(18.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    Surface(
                        shape = RoundedCornerShape(12.dp),
                        color = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(56.dp)
                    ) {
                        Box(contentAlignment = Alignment.Center) {
                            Icon(
                                Icons.Default.DirectionsCar,
                                null,
                                Modifier.size(32.dp),
                                tint = MaterialTheme.colorScheme.onPrimary
                            )
                        }
                    }
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text(
                            item.vehicleNo.ifBlank { "—" },
                            style = MaterialTheme.typography.titleLarge,
                            fontWeight = FontWeight.ExtraBold,
                            color = MaterialTheme.colorScheme.onPrimaryContainer
                        )
                        Text(
                            item.model.ifBlank { "Unknown Model" },
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f)
                        )
                        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            if (item.branchName.isNotBlank()) {
                                Surface(
                                    shape = RoundedCornerShape(6.dp),
                                    color = MaterialTheme.colorScheme.primary
                                ) {
                                    Text(
                                        item.branchName,
                                        Modifier.padding(horizontal = 8.dp, vertical = 3.dp),
                                        style = MaterialTheme.typography.labelSmall,
                                        color = MaterialTheme.colorScheme.onPrimary,
                                        fontWeight = FontWeight.SemiBold
                                    )
                                }
                            }
                            if (item.financer.isNotBlank()) {
                                Surface(
                                    shape = RoundedCornerShape(6.dp),
                                    color = MaterialTheme.colorScheme.secondary
                                ) {
                                    Text(
                                        item.financer,
                                        Modifier.padding(horizontal = 8.dp, vertical = 3.dp),
                                        style = MaterialTheme.typography.labelSmall,
                                        color = MaterialTheme.colorScheme.onSecondary,
                                        fontWeight = FontWeight.SemiBold
                                    )
                                }
                            }
                        }
                    }
                }
            }

            // Sections
            DetailSection(
                title = "Vehicle Info",
                icon = Icons.Default.DirectionsCar,
                entries = listOf(
                    "Vehicle No"    to item.vehicleNo,
                    "Chassis No"    to item.chassisNo,
                    "Engine No"     to item.engineNo,
                    "Model"         to item.model,
                    "Agreement No"  to item.agreementNo,
                    "Release Status" to item.releaseStatus,
                )
            )

            DetailSection(
                title = "Customer Info",
                icon = Icons.Default.Person,
                entries = listOf(
                    "Name"    to item.customerName,
                    "Contact" to item.customerContact,
                    "Address" to item.customerAddress,
                )
            )

            DetailSection(
                title = "Finance & Branch",
                icon = Icons.Default.AccountBalance,
                entries = listOf(
                    "Financer"   to item.financer,
                    "Branch"     to item.branchName,
                    "Region"     to item.region,
                    "Area"       to item.area,
                    "Excel Branch" to item.branchFromExcel,
                )
            )

            DetailSection(
                title = "Contacts",
                icon = Icons.Default.ContactPhone,
                entries = listOf(
                    "1st Contact" to item.firstContact,
                    "2nd Contact" to item.secondContact,
                    "3rd Contact" to item.thirdContact,
                    "Address"     to item.address,
                )
            )

            DetailSection(
                title = "Levels",
                icon = Icons.Default.Layers,
                entries = listOf(
                    "L1"          to item.level1,
                    "L1 Contact"  to item.level1Contact,
                    "L2"          to item.level2,
                    "L2 Contact"  to item.level2Contact,
                    "L3"          to item.level3,
                    "L3 Contact"  to item.level3Contact,
                    "L4"          to item.level4,
                    "L4 Contact"  to item.level4Contact,
                )
            )

            DetailSection(
                title = "Financial Details",
                icon = Icons.Default.CurrencyRupee,
                entries = listOf(
                    "GV"        to item.gv,
                    "OD"        to item.od,
                    "POS"       to item.pos,
                    "TOSS"      to item.toss,
                    "Bucket"    to item.bucket,
                    "Seasoning" to item.seasoning,
                )
            )

            DetailSection(
                title = "Legal & Flags",
                icon = Icons.Default.Gavel,
                entries = listOf(
                    "TBR Flag"  to item.tbrFlag,
                    "Sec 9"     to item.sec9,
                    "Sec 17"    to item.sec17,
                )
            )

            DetailSection(
                title = "Sender & Executive",
                icon = Icons.Default.Badge,
                entries = listOf(
                    "Sender Mail 1"  to item.senderMail1,
                    "Sender Mail 2"  to item.senderMail2,
                    "Executive"      to item.executiveName,
                    "Remark"         to item.remark,
                    "Created On"     to item.createdOn,
                )
            )

            Spacer(Modifier.height(16.dp))
        }
    }
}

@Composable
private fun DetailSection(
    title: String,
    icon: ImageVector,
    entries: List<Pair<String, String>>
) {
    val filled = entries.filter { it.second.isNotBlank() }
    if (filled.isEmpty()) return

    var expanded by remember { mutableStateOf(true) }

    Card(
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surface
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp),
        modifier = Modifier.fillMaxWidth()
    ) {
        Column {
            // Section header — tap to collapse
            Row(
                Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    Icon(
                        icon,
                        null,
                        Modifier.size(18.dp),
                        tint = MaterialTheme.colorScheme.primary
                    )
                    Text(
                        title,
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold
                    )
                }
                IconButton(
                    onClick = { expanded = !expanded },
                    modifier = Modifier.size(24.dp)
                ) {
                    Icon(
                        if (expanded) Icons.Default.ExpandLess else Icons.Default.ExpandMore,
                        null,
                        Modifier.size(18.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }

            AnimatedVisibility(
                visible = expanded,
                enter = expandVertically() + fadeIn(),
                exit = shrinkVertically() + fadeOut()
            ) {
                Column(Modifier.padding(start = 16.dp, end = 16.dp, bottom = 14.dp)) {
                    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.4f))
                    Spacer(Modifier.height(10.dp))
                    filled.forEachIndexed { i, (label, value) ->
                        if (i > 0) Spacer(Modifier.height(8.dp))
                        Row(
                            Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween
                        ) {
                            Text(
                                label,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.weight(0.38f)
                            )
                            Text(
                                value,
                                style = MaterialTheme.typography.bodySmall,
                                fontWeight = FontWeight.Medium,
                                modifier = Modifier.weight(0.62f)
                            )
                        }
                    }
                }
            }
        }
    }
}
