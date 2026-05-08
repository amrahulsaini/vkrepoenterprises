package com.vkenterprises.vras.ui.screens

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import com.vkenterprises.vras.viewmodel.AuthViewModel
import com.vkenterprises.vras.viewmodel.SearchViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConfirmScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val context    = LocalContext.current
    val ui         by searchVm.ui.collectAsState()
    val item       = ui.selectedResult
    val agentName  by authVm.userName.collectAsState(initial = "")
    val agentPhone by authVm.userMobile.collectAsState(initial = "")

    var vehicleAddress by remember { mutableStateOf("") }
    var carriesGoods   by remember { mutableStateOf("") }

    fun buildMessage(): String = buildString {
        appendLine("Respected sir,")
        appendLine("Customer Name: ${item?.customerName ?: ""}")
        appendLine("Vehicle No: ${item?.vehicleNo ?: ""}")
        appendLine("Model/Maker: ${item?.model ?: ""}")
        appendLine("Chassis No: ${item?.chassisNo ?: ""}")
        appendLine("Engine No: ${item?.engineNo ?: ""}")
        appendLine("Vehicle location: *${vehicleAddress.trim()}*")
        appendLine("Load details: ${carriesGoods.trim().ifEmpty { "—" }}")
        appendLine()
        appendLine("Status: Please confirm this vehicle.")
        if (agentName.isNotBlank() && agentPhone.isNotBlank())
            appendLine("$agentName - $agentPhone")
        append("Agency Name: V K Enterprises")
    }

    fun sendWhatsApp() {
        val msg     = buildMessage()
        val intent  = Intent(Intent.ACTION_VIEW,
            Uri.parse("https://wa.me/?text=${Uri.encode(msg)}"))
        context.startActivity(intent)
    }

    fun sendSms() {
        // SMS version — strip WhatsApp *bold* markers
        val msg    = buildMessage().replace("*", "")
        val intent = Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:"))
        intent.putExtra("sms_body", msg)
        context.startActivity(intent)
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Send Confirmation", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, null)
                    }
                }
            )
        }
    ) { pad ->
        if (item == null) {
            Box(Modifier.fillMaxSize().padding(pad), contentAlignment = Alignment.Center) {
                Text("No vehicle selected.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            return@Scaffold
        }

        Column(
            Modifier
                .padding(pad)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // ── Vehicle summary ──────────────────────────────────────────
            Card(
                shape = RoundedCornerShape(12.dp),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    SummaryRow("Customer",   item.customerName)
                    SummaryRow("Vehicle No", item.vehicleNo,  mono = true)
                    SummaryRow("Chassis",    item.chassisNo,  mono = true)
                    SummaryRow("Engine",     item.engineNo,   mono = true)
                    SummaryRow("Model",      item.model)
                }
            }

            // ── Inputs ───────────────────────────────────────────────────
            OutlinedTextField(
                value = vehicleAddress,
                onValueChange = { vehicleAddress = it },
                label = { Text("Vehicle Location / Address") },
                leadingIcon = { Icon(Icons.Default.LocationOn, null) },
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(10.dp),
                maxLines = 3
            )

            OutlinedTextField(
                value = carriesGoods,
                onValueChange = { carriesGoods = it },
                label = { Text("Carries Goods / Load Details") },
                leadingIcon = { Icon(Icons.Default.LocalShipping, null) },
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(10.dp),
                maxLines = 2
            )

            // ── Message preview ──────────────────────────────────────────
            Card(
                shape = RoundedCornerShape(10.dp),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(14.dp)) {
                    Text("Message Preview",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(8.dp))
                    Text(buildMessage(),
                        style = MaterialTheme.typography.bodySmall,
                        fontFamily = FontFamily.Monospace,
                        lineHeight = 18.sp)
                }
            }

            // ── Send buttons ─────────────────────────────────────────────
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(
                    onClick = { sendWhatsApp() },
                    modifier = Modifier.weight(1f).height(52.dp),
                    shape = RoundedCornerShape(10.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = Color(0xFF25D366),
                        contentColor   = Color.White)
                ) {
                    Icon(Icons.Default.Chat, null, Modifier.size(18.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("WhatsApp", fontWeight = FontWeight.Bold)
                }

                Button(
                    onClick = { sendSms() },
                    modifier = Modifier.weight(1f).height(52.dp),
                    shape = RoundedCornerShape(10.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.secondary,
                        contentColor   = MaterialTheme.colorScheme.onSecondary)
                ) {
                    Icon(Icons.Default.Sms, null, Modifier.size(18.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("SMS", fontWeight = FontWeight.Bold)
                }
            }

            Spacer(Modifier.height(16.dp))
        }
    }
}

@Composable
private fun SummaryRow(label: String, value: String, mono: Boolean = false) {
    if (value.isBlank()) return
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.35f))
        Text(value,
            style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Medium,
            fontFamily = if (mono) FontFamily.Monospace else FontFamily.Default,
            modifier = Modifier.weight(0.65f))
    }
}
