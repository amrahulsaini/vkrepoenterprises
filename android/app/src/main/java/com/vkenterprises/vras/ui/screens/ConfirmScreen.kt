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
    val actionType = ui.actionType
    val agentName  by authVm.userName.collectAsState(initial = "")
    val agentPhone by authVm.userMobile.collectAsState(initial = "")
    val isAdmin    by authVm.isAdmin.collectAsState(initial = false)

    // SMS recipient checkboxes — pre-tick any contact that has a number
    var chkL1 by remember { mutableStateOf(item?.level1Contact?.isNotBlank() == true) }
    var chkL2 by remember { mutableStateOf(item?.level2Contact?.isNotBlank() == true) }
    var chkL3 by remember { mutableStateOf(item?.level3Contact?.isNotBlank() == true) }
    var chkL4 by remember { mutableStateOf(item?.level4Contact?.isNotBlank() == true) }
    var chkC1 by remember { mutableStateOf(item?.firstContact?.isNotBlank()  == true) }
    var chkC2 by remember { mutableStateOf(item?.secondContact?.isNotBlank() == true) }

    var vehicleAddress by remember { mutableStateOf("") }
    var carriesGoods   by remember { mutableStateOf("") }

    val screenTitle = when (actionType) {
        "okrepo" -> "OK for Repo"
        "cancel" -> "Send Cancellation"
        else     -> "Send Confirmation"
    }

    fun buildMessage(): String = buildString {
        when (actionType) {
            "cancel" -> {
                appendLine("*Respected sir,*")
                appendLine("We regret to inform you that the above traced vehicle has been *CANCELLED*.")
                appendLine()
            }
            "okrepo" -> {
                appendLine("*Respected sir,*")
                appendLine("The above traced vehicle has been confirmed for *OK for Repo*. Please proceed accordingly.")
                appendLine()
            }
            else -> {
                appendLine("*Respected sir,*")
                appendLine("A Vehicle has been traced out by our ground team. The details of the vehicle and customer are as below.")
                appendLine()
            }
        }

        // On the admin side, empty fields are written as "null" so the admin
        // can see exactly which data is missing. Non-admins skip empty fields.
        fun line(label: String, value: String?) {
            val v = value?.trim().orEmpty()
            if (v.isNotBlank()) appendLine("$label: *$v*")
            else if (isAdmin)   appendLine("$label: *null*")
        }

        line("Loan No",       item?.agreementNo)
        line("Customer Name", item?.customerName)
        line("Branch",        item?.branchName)
        line("Vehicle No",    item?.vehicleNo)
        line("Vehicle Model", item?.model)
        line("Chassis No",    item?.chassisNo)
        line("Engine No",     item?.engineNo)

        fun levelLine(label: String, name: String?, contact: String?) {
            val n = name?.trim().orEmpty(); val c = contact?.trim().orEmpty()
            if (n.isNotBlank() || c.isNotBlank()) appendLine("$label: *$n - $c*")
            else if (isAdmin)                     appendLine("$label: *null*")
        }
        levelLine("Level1", item?.level1, item?.level1Contact)
        levelLine("Level2", item?.level2, item?.level2Contact)
        levelLine("Level3", item?.level3, item?.level3Contact)
        if (vehicleAddress.isNotBlank())
            appendLine("Vehicle location: *${vehicleAddress.trim()}*")
        if (carriesGoods.isNotBlank())
            appendLine("Load details: *${carriesGoods.trim()}*")

        appendLine()
        if (actionType == "confirm") {
            appendLine("We urgently need you to confirm the status of this vehicle, whether it is to be Repo released.")
        }
        append("*V K Enterprises*")
        if (agentName.isNotBlank()) {
            append(" contact person")
            append(" — $agentName")
        }
        if (agentPhone.isNotBlank()) append(" | $agentPhone")
    }

    fun checkedNumbers(): List<String> = buildList {
        if (chkL1 && item?.level1Contact?.isNotBlank() == true) add(item.level1Contact)
        if (chkL2 && item?.level2Contact?.isNotBlank() == true) add(item.level2Contact)
        if (chkL3 && item?.level3Contact?.isNotBlank() == true) add(item.level3Contact)
        if (chkL4 && item?.level4Contact?.isNotBlank() == true) add(item.level4Contact)
        if (chkC1 && item?.firstContact?.isNotBlank()  == true) add(item.firstContact)
        if (chkC2 && item?.secondContact?.isNotBlank() == true) add(item.secondContact)
    }

    fun sendWhatsApp() {
        val msg    = buildMessage()
        val intent = Intent(Intent.ACTION_VIEW,
            Uri.parse("https://wa.me/?text=${Uri.encode(msg)}"))
        context.startActivity(intent)
    }

    fun sendSms() {
        val msg   = buildMessage().replace("*", "")
        val nums  = checkedNumbers()
        val uri   = if (nums.isNotEmpty())
            Uri.parse("smsto:${nums.joinToString(";")}")
        else
            Uri.parse("smsto:")
        val intent = Intent(Intent.ACTION_SENDTO, uri)
        intent.putExtra("sms_body", msg)
        context.startActivity(intent)
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(screenTitle, fontWeight = FontWeight.Bold) },
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
            // ── Vehicle summary card ──────────────────────────────────────
            Card(
                shape  = RoundedCornerShape(12.dp),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    SummaryRow("Agreement", item.agreementNo, mono = true)
                    SummaryRow("Customer",  item.customerName)
                    SummaryRow("Vehicle No",item.vehicleNo,   mono = true)
                    SummaryRow("Chassis",   item.chassisNo,   mono = true)
                    SummaryRow("Engine",    item.engineNo,    mono = true)
                    SummaryRow("Model",     item.model)
                    SummaryRow("BKT",       item.bucket)
                    SummaryRow("OD",        item.od)
                    SummaryRow("Branch",    item.branchName)
                    SummaryRow("Financer",  item.financer)
                }
            }

            // ── SMS recipients ────────────────────────────────────────────
            val hasAnyContact = listOf(
                item.level1Contact, item.level2Contact,
                item.level3Contact, item.level4Contact,
                item.firstContact,  item.secondContact
            ).any { !it.isNullOrBlank() }

            Card(
                shape  = RoundedCornerShape(12.dp),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(
                        "Select Recipients for SMS",
                        style    = MaterialTheme.typography.labelMedium,
                        color    = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.Bold,
                        modifier = Modifier.padding(bottom = 4.dp)
                    )

                    if (!hasAnyContact) {
                        Text(
                            "No contact numbers available for this vehicle.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    } else {
                        if (!item.level1Contact.isNullOrBlank())
                            ContactCheckRow("Level 1", item.level1.orEmpty(), item.level1Contact, chkL1) { chkL1 = it }
                        if (!item.level2Contact.isNullOrBlank())
                            ContactCheckRow("Level 2", item.level2.orEmpty(), item.level2Contact, chkL2) { chkL2 = it }
                        if (!item.level3Contact.isNullOrBlank())
                            ContactCheckRow("Level 3", item.level3.orEmpty(), item.level3Contact, chkL3) { chkL3 = it }
                        if (!item.level4Contact.isNullOrBlank())
                            ContactCheckRow("Level 4", item.level4.orEmpty(), item.level4Contact, chkL4) { chkL4 = it }
                        if (!item.firstContact.isNullOrBlank())
                            ContactCheckRow("Contact 1 (Finance/Branch)", item.financer.orEmpty(), item.firstContact, chkC1) { chkC1 = it }
                        if (!item.secondContact.isNullOrBlank())
                            ContactCheckRow("Contact 2 (Finance/Branch)", item.financer.orEmpty(), item.secondContact, chkC2) { chkC2 = it }
                    }
                }
            }

            // ── Inputs ────────────────────────────────────────────────────
            OutlinedTextField(
                value = vehicleAddress,
                onValueChange = { vehicleAddress = it },
                label         = { Text("Vehicle Location / Address") },
                leadingIcon   = { Icon(Icons.Default.LocationOn, null) },
                modifier      = Modifier.fillMaxWidth(),
                shape         = RoundedCornerShape(10.dp),
                maxLines      = 3
            )

            OutlinedTextField(
                value = carriesGoods,
                onValueChange = { carriesGoods = it },
                label         = { Text("Carries Goods / Load Details") },
                leadingIcon   = { Icon(Icons.Default.LocalShipping, null) },
                modifier      = Modifier.fillMaxWidth(),
                shape         = RoundedCornerShape(10.dp),
                maxLines      = 2
            )

            // ── Message preview ───────────────────────────────────────────
            Card(
                shape  = RoundedCornerShape(10.dp),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(14.dp)) {
                    Text(
                        "Message Preview",
                        style      = MaterialTheme.typography.labelSmall,
                        color      = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(Modifier.height(8.dp))
                    Text(
                        buildMessage(),
                        style      = MaterialTheme.typography.bodySmall,
                        fontFamily = FontFamily.Monospace,
                        lineHeight = 18.sp
                    )
                }
            }

            // ── Send buttons ──────────────────────────────────────────────
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(
                    onClick  = { sendWhatsApp() },
                    modifier = Modifier.weight(1f).height(52.dp),
                    shape    = RoundedCornerShape(10.dp),
                    colors   = ButtonDefaults.buttonColors(
                        containerColor = Color(0xFF25D366),
                        contentColor   = Color.White)
                ) {
                    Icon(Icons.Default.Chat, null, Modifier.size(18.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("WhatsApp", fontWeight = FontWeight.Bold)
                }

                Button(
                    onClick  = { sendSms() },
                    modifier = Modifier.weight(1f).height(52.dp),
                    shape    = RoundedCornerShape(10.dp),
                    colors   = ButtonDefaults.buttonColors(
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
private fun ContactCheckRow(
    label: String,
    name: String?,
    phone: String,
    checked: Boolean,
    onChecked: (Boolean) -> Unit
) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier          = Modifier.fillMaxWidth()
    ) {
        Checkbox(checked = checked, onCheckedChange = onChecked)
        Column(Modifier.weight(1f).padding(start = 4.dp)) {
            Text(
                "$label${if (!name.isNullOrBlank()) " — $name" else ""}",
                style      = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.Medium
            )
            Text(
                phone,
                style      = MaterialTheme.typography.labelSmall,
                color      = MaterialTheme.colorScheme.primary,
                fontFamily = FontFamily.Monospace
            )
        }
    }
}

@Composable
private fun SummaryRow(label: String, value: String?, mono: Boolean = false) {
    if (value.isNullOrBlank()) return
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(
            label,
            style    = MaterialTheme.typography.labelSmall,
            color    = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.35f)
        )
        Text(
            value,
            style      = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Medium,
            fontFamily = if (mono) FontFamily.Monospace else FontFamily.Default,
            modifier   = Modifier.weight(0.65f)
        )
    }
}
