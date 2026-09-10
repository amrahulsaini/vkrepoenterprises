package com.vkenterprises.crmrs.ui.screens

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.ConfirmationLog
import com.vkenterprises.crmrs.ui.theme.RobotoFamily
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import kotlinx.coroutines.flow.first
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

private val AMBER_DEEP = Color(0xFFB45309)
private val AMBER_MID  = Color(0xFFD97706)
private val AMBER_PALE = Color(0xFFFFFBEB)

private val DAY_FMT   = DateTimeFormatter.ofPattern("dd MMM yyyy")
private val STAMP_FMT = DateTimeFormatter.ofPattern("dd MMM yyyy, hh:mm a")

internal fun parseServerInstant(raw: String?): java.time.LocalDateTime? {
    if (raw.isNullOrBlank()) return null
    return runCatching {
        Instant.parse(raw).atZone(ZoneId.systemDefault()).toLocalDateTime()
    }.recoverCatching {
        java.time.LocalDateTime.parse(raw.substringBefore('Z').trim())
    }.getOrNull()
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConfirmationsScreen(vm: AuthViewModel, nav: NavController) {
    val context = LocalContext.current

    var from    by remember { mutableStateOf<LocalDate?>(LocalDate.now().minusDays(29)) }
    var to      by remember { mutableStateOf<LocalDate?>(LocalDate.now()) }
    var items   by remember { mutableStateOf<List<ConfirmationLog>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var error   by remember { mutableStateOf<String?>(null) }
    var pickerOpen by remember { mutableStateOf(false) }
    var expanded   by remember { mutableStateOf<Long?>(null) }
    var photo      by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(from, to) {
        loading = true
        error = null
        runCatching {
            val uid = vm.userId.first()
            val r = ApiClient.api.getConfirmations(
                uid,
                from?.format(DateTimeFormatter.ISO_LOCAL_DATE),
                to?.format(DateTimeFormatter.ISO_LOCAL_DATE)
            )
            if (r.isSuccessful) items = r.body()?.items.orEmpty()
            else error = "Couldn't load your confirmations."
        }.onFailure { error = "Couldn't reach the server." }
        loading = false
    }

    val rangeLabel = when {
        from == null && to == null -> "All time"
        from != null && to != null -> "${from!!.format(DAY_FMT)} — ${to!!.format(DAY_FMT)}"
        from != null               -> "From ${from!!.format(DAY_FMT)}"
        else                       -> "Up to ${to!!.format(DAY_FMT)}"
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("My Confirmations", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                },
                actions = {
                    IconButton(onClick = { pickerOpen = true }) {
                        Icon(Icons.Default.DateRange, "Pick a date range", tint = AMBER_DEEP)
                    }
                }
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {

            Surface(
                color = AMBER_PALE,
                modifier = Modifier.fillMaxWidth().padding(16.dp),
                shape = RoundedCornerShape(14.dp),
                border = androidx.compose.foundation.BorderStroke(2.dp, Color(0xFFF59E0B))
            ) {
                Row(
                    Modifier.padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(Modifier.weight(1f)) {
                        Text("CONFIRMATIONS SENT",
                            style = MaterialTheme.typography.labelMedium,
                            fontWeight = FontWeight.Bold,
                            color = Color(0xFF78350F))
                        Spacer(Modifier.height(2.dp))
                        Text(rangeLabel,
                            style = MaterialTheme.typography.labelSmall,
                            color = Color(0xFF92400E))
                        Spacer(Modifier.height(8.dp))
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            AssistChip(
                                onClick = { pickerOpen = true },
                                label = { Text("Change range", fontSize = 11.sp) },
                                leadingIcon = { Icon(Icons.Default.EditCalendar, null, Modifier.size(14.dp)) }
                            )
                            AssistChip(
                                onClick = { from = null; to = null },
                                label = { Text("All time", fontSize = 11.sp) }
                            )
                        }
                    }
                    Text(
                        if (loading) "…" else items.size.toString(),
                        fontSize = 44.sp,
                        fontWeight = FontWeight.ExtraBold,
                        color = AMBER_DEEP
                    )
                }
            }

            when {
                loading -> Box(Modifier.fillMaxWidth().padding(40.dp), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(color = AMBER_MID)
                }
                error != null -> Text(error!!,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium,
                    modifier = Modifier.padding(horizontal = 16.dp))
                items.isEmpty() -> Column(
                    Modifier.fillMaxWidth().padding(32.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(Icons.Default.MarkChatRead, null,
                        tint = MaterialTheme.colorScheme.outlineVariant,
                        modifier = Modifier.size(48.dp))
                    Spacer(Modifier.height(10.dp))
                    Text("Nothing confirmed in this range.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                else -> LazyColumn(
                    contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 24.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    items(items, key = { it.id }) { log ->
                        ConfirmationCard(
                            log = log,
                            open = expanded == log.id,
                            onToggle = { expanded = if (expanded == log.id) null else log.id },
                            onPhoto = { photo = it },
                            onMap = { link ->
                                runCatching {
                                    context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(link)))
                                }
                            }
                        )
                    }
                }
            }
        }
    }

    if (pickerOpen) {
        val state = rememberDateRangePickerState(
            initialSelectedStartDateMillis = from?.atStartOfDay(ZoneId.of("UTC"))?.toInstant()?.toEpochMilli(),
            initialSelectedEndDateMillis   = to?.atStartOfDay(ZoneId.of("UTC"))?.toInstant()?.toEpochMilli()
        )
        DatePickerDialog(
            onDismissRequest = { pickerOpen = false },
            confirmButton = {
                TextButton(onClick = {
                    fun millisToDay(m: Long?) = m?.let {
                        Instant.ofEpochMilli(it).atZone(ZoneId.of("UTC")).toLocalDate()
                    }
                    val s = millisToDay(state.selectedStartDateMillis)
                    val e = millisToDay(state.selectedEndDateMillis)
                    if (s != null) { from = s; to = e ?: s }
                    pickerOpen = false
                }) { Text("Apply") }
            },
            dismissButton = { TextButton(onClick = { pickerOpen = false }) { Text("Cancel") } }
        ) {
            DateRangePicker(state = state, showModeToggle = false)
        }
    }

    photo?.let { url ->
        PhotoDialog(url) { photo = null }
    }
}

@Composable
private fun PhotoDialog(url: String, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = { TextButton(onClick = onDismiss) { Text("Close") } },
        title = { Text("Confirmation photo", fontWeight = FontWeight.Bold) },
        text = {
            AsyncImage(
                model = url,
                contentDescription = "Confirmation photo",
                contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxWidth().heightIn(max = 360.dp).clip(RoundedCornerShape(10.dp))
            )
        }
    )
}

@Composable
private fun ConfirmationCard(
    log: ConfirmationLog,
    open: Boolean,
    onToggle: () -> Unit,
    onPhoto: (String) -> Unit,
    onMap: (String) -> Unit
) {
    val stamp = parseServerInstant(log.confirmedAt)?.format(STAMP_FMT) ?: "—"
    val action = when (log.actionType) {
        "okrepo" -> "OK for Repo"
        "cancel" -> "Cancellation"
        else     -> "Confirmation"
    }
    val channel = if (log.channel.equals("sms", true)) "SMS" else "WhatsApp"

    Card(
        onClick = onToggle,
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        border = androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
        elevation = CardDefaults.cardElevation(0.dp),
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(
                        log.vehicleNo?.takeIf { it.isNotBlank() } ?: log.chassisNo.orEmpty().ifBlank { "—" },
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        fontFamily = RobotoFamily
                    )
                    Text(stamp,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Surface(
                    color = AMBER_PALE,
                    shape = RoundedCornerShape(50),
                    border = androidx.compose.foundation.BorderStroke(1.dp, Color(0xFFFCD34D))
                ) {
                    Text(action,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp),
                        style = MaterialTheme.typography.labelSmall,
                        color = AMBER_DEEP,
                        fontWeight = FontWeight.Bold)
                }
                Spacer(Modifier.width(6.dp))
                Icon(
                    if (channel == "SMS") Icons.Default.Sms else Icons.Default.Chat,
                    channel,
                    tint = if (channel == "SMS") MaterialTheme.colorScheme.secondary else Color(0xFF25D366),
                    modifier = Modifier.size(18.dp)
                )
            }

            if (!log.customerName.isNullOrBlank())
                LogRow("Customer", log.customerName)

            if (open) {
                LogRow("Chassis",  log.chassisNo)
                LogRow("Engine",   log.engineNo)
                LogRow("Model",    log.model)
                LogRow("Loan No",  log.agreementNo)
                LogRow("Finance",  log.financer)
                LogRow("Location", log.address)
                LogRow("Load",     log.loadDetails)
                LogRow("Sent via", channel)

                if (!log.imageUrl.isNullOrBlank()) {
                    Spacer(Modifier.height(4.dp))
                    AsyncImage(
                        model = log.imageUrl,
                        contentDescription = "Confirmation photo",
                        contentScale = ContentScale.Crop,
                        modifier = Modifier
                            .fillMaxWidth().height(160.dp)
                            .clip(RoundedCornerShape(8.dp))
                            .clickable { onPhoto(log.imageUrl) }
                    )
                }

                if (!log.mapLink.isNullOrBlank()) {
                    TextButton(onClick = { onMap(log.mapLink) }, contentPadding = PaddingValues(0.dp)) {
                        Icon(Icons.Default.Place, null, Modifier.size(16.dp))
                        Spacer(Modifier.width(4.dp))
                        Text("Open location on map", style = MaterialTheme.typography.labelMedium)
                    }
                }

                if (!log.messageText.isNullOrBlank()) {
                    Spacer(Modifier.height(2.dp))
                    Text("Message sent",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.Bold)
                    Surface(
                        color = MaterialTheme.colorScheme.surfaceVariant,
                        shape = RoundedCornerShape(8.dp),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text(
                            log.messageText.replace("*", ""),
                            modifier = Modifier
                                .heightIn(max = 200.dp)
                                .verticalScroll(rememberScrollState())
                                .padding(10.dp),
                            style = MaterialTheme.typography.bodySmall,
                            fontFamily = RobotoFamily,
                            lineHeight = 17.sp
                        )
                    }
                }
            }

            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.End,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(if (open) "Hide details" else "View full log",
                    style = MaterialTheme.typography.labelSmall,
                    color = AMBER_MID,
                    fontWeight = FontWeight.Bold)
                Icon(
                    if (open) Icons.Default.ExpandLess else Icons.Default.ExpandMore,
                    null, tint = AMBER_MID, modifier = Modifier.size(18.dp)
                )
            }
        }
    }
}

@Composable
private fun LogRow(label: String, value: String?) {
    if (value.isNullOrBlank()) return
    Row(Modifier.fillMaxWidth()) {
        Text(label,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.32f))
        Text(value,
            style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Medium,
            fontFamily = RobotoFamily,
            modifier = Modifier.weight(0.68f))
    }
}
