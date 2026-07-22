package com.vkenterprises.crmrs.ui.screens

import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import androidx.core.content.FileProvider
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.ConfirmCaptureRequest
import com.vkenterprises.crmrs.utils.compressImageToBase64
import com.vkenterprises.crmrs.utils.getCurrentLocation
import com.vkenterprises.crmrs.utils.googleMapsLink
import com.vkenterprises.crmrs.utils.reverseGeocodeAddress
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.SearchViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConfirmScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val context    = LocalContext.current
    val ui         by searchVm.ui.collectAsState()
    val skinny     = ui.selectedResult
    val full       = ui.fullRecord
    val item       = full?.takeIf {
        skinny == null || it.vehicleNo == skinny.vehicleNo || it.chassisNo == skinny.chassisNo
    } ?: skinny
    val actionType = ui.actionType
    val agentName  by authVm.userName.collectAsState(initial = "")
    val agentPhone by authVm.userMobile.collectAsState(initial = "")
    val isAdmin    by authVm.isAdmin.collectAsState(initial = false)
    val userId     by authVm.userId.collectAsState(initial = -1L)

    var agencyName by remember { mutableStateOf(BuildConfig.AGENCY_NAME) }
    LaunchedEffect(Unit) {
        runCatching {
            val r = com.vkenterprises.crmrs.data.api.ApiClient.api.getAgencyInfo()
            if (r.isSuccessful) r.body()?.name?.takeIf { it.isNotBlank() }?.let { agencyName = it }
        }
    }

    LaunchedEffect(skinny?.id, full?.id, userId) {
        val s = skinny ?: return@LaunchedEffect
        val haveFull = full != null && (full.vehicleNo == s.vehicleNo || full.chassisNo == s.chassisNo)
        if (!haveFull && userId > 0L) searchVm.fetchFullRecord(s.id, userId)
    }

    var chkL1 by remember(item?.id) { mutableStateOf(item?.level1Contact?.isNotBlank() == true) }
    var chkL2 by remember(item?.id) { mutableStateOf(item?.level2Contact?.isNotBlank() == true) }
    var chkL3 by remember(item?.id) { mutableStateOf(item?.level3Contact?.isNotBlank() == true) }
    var chkL4 by remember(item?.id) { mutableStateOf(item?.level4Contact?.isNotBlank() == true) }
    var chkC1 by remember(item?.id) { mutableStateOf(item?.firstContact?.isNotBlank()  == true) }
    var chkC2 by remember(item?.id) { mutableStateOf(item?.secondContact?.isNotBlank() == true) }

    var vehicleAddress by remember { mutableStateOf("") }
    var carriesGoods   by remember { mutableStateOf("") }
    var vehicleLat by remember { mutableStateOf<Double?>(null) }
    var vehicleLng by remember { mutableStateOf<Double?>(null) }
    var locating   by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun fetchLocation() {
        if (locating) return
        locating = true
        scope.launch {
            val loc = getCurrentLocation(context)
            if (loc != null) {
                vehicleLat = loc.latitude
                vehicleLng = loc.longitude
                val addr = reverseGeocodeAddress(context, loc.latitude, loc.longitude)
                if (!addr.isNullOrBlank() && vehicleAddress.isBlank()) vehicleAddress = addr
            }
            locating = false
        }
    }

    LaunchedEffect(item?.id) {
        if (item == null) return@LaunchedEffect
        if (authVm.isAdmin.first()) return@LaunchedEffect
        fetchLocation()
    }

    val mapLink: String? = if (isAdmin) null
        else vehicleLat?.let { la -> vehicleLng?.let { ln -> googleMapsLink(la, ln) } }

    val screenTitle = when (actionType) {
        "okrepo" -> "OK for Repo"
        "cancel" -> "Send Cancellation"
        else     -> "Send Confirmation"
    }

    fun buildUserMessage(): String = buildString {
        val status = when (actionType) {
            "okrepo" -> "Ok for repo."
            "cancel" -> "Cancel"
            else     -> "Please confirm this vehicle."
        }
        appendLine("*Respected sir,*")
        appendLine("Customer Name: *${item?.customerName?.trim().orEmpty().ifBlank { "-" }}*")
        appendLine("Vehicle No: *${item?.vehicleNo?.trim().orEmpty()}*")
        appendLine("Model/Maker: *${item?.model?.trim().orEmpty().ifBlank { "-" }}*")
        appendLine("Chassis No: *${item?.chassisNo?.trim().orEmpty()}*")
        appendLine("Engine No: *${item?.engineNo?.trim().orEmpty().ifBlank { "-" }}*")
        appendLine("Vehicle location: *${vehicleAddress.trim().ifBlank { "-" }}*")
        mapLink?.let { appendLine("Location on Map: $it") }
        appendLine("Load details: *${carriesGoods.trim().ifBlank { "-" }}*")
        appendLine()
        appendLine("Status: *$status*")
        val person = listOf(agentName.trim(), agentPhone.trim()).filter { it.isNotBlank() }.joinToString(" - ")
        if (person.isNotBlank()) appendLine(person)
        append("Agency Name: *${agencyName}*")
    }

    fun buildMessage(): String {
        if (!isAdmin) return buildUserMessage()
        return buildString {
        appendLine("*Respected sir,*")
        when (actionType) {
            "okrepo" -> appendLine("This vehicle is confirmed OK for repo. The details of the vehicle and customer are as below.")
            "cancel" -> appendLine("Please note the status update for the vehicle below.")
            else     -> appendLine("A Vehicle has been traced out by our ground team. The details of the vehicle and customer are as below.")
        }
        appendLine()

        fun line(label: String, value: String?) {
            val v = value?.trim().orEmpty()
            if (v.isNotBlank()) appendLine("$label: *$v*")
            else if (isAdmin)   appendLine("$label: *null*")
        }

        line("Loan No",       item?.agreementNo)
        line("Branch",        item?.branchFromExcel)
        line("Customer Name", item?.customerName)
        line("Vehicle No",    item?.vehicleNo)
        line("Model/Maker",   item?.model)
        line("Chassis No",    item?.chassisNo)
        line("Engine No",     item?.engineNo)
        line("BKT",           item?.bucket)
        line("OD",            item?.od)
        appendLine("Vehicle location: *${vehicleAddress.trim().ifBlank { "-" }}*")
        appendLine("Load details: *${carriesGoods.trim().ifBlank { "-" }}*")

        fun levelLine(label: String, name: String?, contact: String?) {
            val n = name?.trim().orEmpty(); val c = contact?.trim().orEmpty()
            val content = listOf(n, c).filter { it.isNotBlank() }.joinToString(" - ")
            if (content.isNotBlank()) appendLine("$label: *$content*")
            else if (isAdmin)         appendLine("$label: *null*")
        }
        levelLine("Level1", item?.level1, item?.level1Contact)
        levelLine("Level2", item?.level2, item?.level2Contact)
        levelLine("Level3", item?.level3, item?.level3Contact)

        appendLine()
        val closing = when (actionType) {
            "okrepo" -> "Kindly proceed — this vehicle is OK for repo."
            "cancel" -> "Kindly note: this vehicle stands cancelled / not confirmed."
            else     -> "We urgently need you to confirm the status of this vehicle, whether it is to be Repo released."
        }
        append(closing)
        append(" *${agencyName}*")
        val person = listOf(agentName.trim(), agentPhone.trim()).filter { it.isNotBlank() }.joinToString(" - ")
        if (person.isNotBlank()) append(" $person")
        }
    }

    fun checkedNumbers(): List<String> = if (!isAdmin) emptyList() else buildList {
        if (chkL1 && item?.level1Contact?.isNotBlank() == true) add(item.level1Contact)
        if (chkL2 && item?.level2Contact?.isNotBlank() == true) add(item.level2Contact)
        if (chkL3 && item?.level3Contact?.isNotBlank() == true) add(item.level3Contact)
        if (chkL4 && item?.level4Contact?.isNotBlank() == true) add(item.level4Contact)
        if (chkC1 && item?.firstContact?.isNotBlank()  == true) add(item.firstContact)
        if (chkC2 && item?.secondContact?.isNotBlank() == true) add(item.secondContact)
    }

    fun sendWhatsApp() {
        val msg  = buildMessage()
        val base = Intent(Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(Intent.EXTRA_TEXT, msg)
        }
        val pm = context.packageManager
        val target = listOf("com.whatsapp", "com.whatsapp.w4b").firstOrNull { p ->
            runCatching { pm.getPackageInfo(p, 0); true }.getOrDefault(false)
        }
        val launch = if (target != null) Intent(base).setPackage(target)
                     else Intent.createChooser(base, "Share confirmation via")
        runCatching { context.startActivity(launch) }.onFailure {
            runCatching { context.startActivity(Intent.createChooser(base, "Share confirmation via")) }
        }
    }

    // ── Mandatory confirm photo (non-admin field agents) ────────────────────
    var photoUri  by remember(item?.id) { mutableStateOf<Uri?>(null) }
    var photoFile by remember(item?.id) { mutableStateOf<File?>(null) }
    var sending   by remember { mutableStateOf(false) }

    fun newPhotoTarget(): Uri {
        val f = File(context.cacheDir, "confirm_${System.currentTimeMillis()}.jpg")
        photoFile = f
        return FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", f)
    }

    val cameraLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.TakePicture()
    ) { ok ->
        if (ok && photoFile?.let { it.exists() && it.length() > 0 } == true) {
            photoUri = FileProvider.getUriForFile(
                context, "${context.packageName}.fileprovider", photoFile!!)
        } else {
            photoUri = null
            photoFile = null
        }
    }

    fun sendWhatsAppWithImage(imageUri: Uri) {
        val msg  = buildMessage()
        val base = Intent(Intent.ACTION_SEND).apply {
            type = "image/jpeg"
            putExtra(Intent.EXTRA_STREAM, imageUri)
            putExtra(Intent.EXTRA_TEXT, msg)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        val pm = context.packageManager
        val target = listOf("com.whatsapp", "com.whatsapp.w4b").firstOrNull { p ->
            runCatching { pm.getPackageInfo(p, 0); true }.getOrDefault(false)
        }
        val launch = if (target != null) Intent(base).setPackage(target)
                     else Intent.createChooser(base, "Share confirmation via")
        runCatching { context.startActivity(launch) }.onFailure {
            runCatching { context.startActivity(Intent.createChooser(base, "Share confirmation via")) }
        }
    }

    // Non-admin "Send Confirm": the photo is mandatory. On tap we save the
    // capture to the server (confirm_captures) and then open WhatsApp with the
    // image + message. A server hiccup must not strand a field agent, so if the
    // save fails we still send — the photo is attached to WhatsApp regardless.
    fun confirmWithPhoto() {
        val uri = photoUri ?: return
        if (sending) return
        sending = true
        scope.launch {
            runCatching {
                val b64 = withContext(Dispatchers.IO) { compressImageToBase64(context, uri) }
                if (b64 != null && userId > 0L) {
                    ApiClient.api.confirmCapture(
                        userId,
                        ConfirmCaptureRequest(
                            vehicleNo     = item?.vehicleNo,
                            chassisNo     = item?.chassisNo,
                            imageBase64   = b64,
                            capturedAtIso = java.time.Instant.now().toString()
                        )
                    )
                }
            }.onFailure {
                Toast.makeText(context,
                    "Couldn't save the photo to the server, sending anyway.",
                    Toast.LENGTH_SHORT).show()
            }
            sending = false
            sendWhatsAppWithImage(uri)
        }
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

    val pageBg = if (isAdmin) MaterialTheme.colorScheme.background else Color.White
    Scaffold(
        containerColor = pageBg,
        topBar = {
            TopAppBar(
                colors = TopAppBarDefaults.topAppBarColors(containerColor = pageBg),
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
            Card(
                shape  = RoundedCornerShape(12.dp),
                colors = CardDefaults.cardColors(containerColor = if (isAdmin) MaterialTheme.colorScheme.surfaceVariant else Color.White),
                elevation = CardDefaults.cardElevation(if (isAdmin) 1.dp else 0.dp),
                border = if (isAdmin) null else androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    SummaryRow("Customer",  item.customerName,            alwaysShow = !isAdmin)
                    SummaryRow("Vehicle No",item.vehicleNo,   mono = true, alwaysShow = !isAdmin)
                    SummaryRow("Chassis",   item.chassisNo,   mono = true, alwaysShow = !isAdmin)
                    SummaryRow("Engine",    item.engineNo,    mono = true, alwaysShow = !isAdmin)
                    SummaryRow("Model",     item.model,                    alwaysShow = !isAdmin)
                    if (isAdmin) {
                        SummaryRow("Agreement", item.agreementNo, mono = true)
                        SummaryRow("BKT",       item.bucket)
                        SummaryRow("OD",        item.od)
                        SummaryRow("Branch",    item.branchName)
                        SummaryRow("Financer",  item.financer)
                    }
                }
            }

            if (isAdmin) {
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
            }

            OutlinedTextField(
                value = vehicleAddress,
                onValueChange = { vehicleAddress = it },
                label         = { Text(if (locating) "Fetching current location…" else "Vehicle Location / Address") },
                leadingIcon   = { Icon(Icons.Default.LocationOn, null) },
                trailingIcon  = if (isAdmin) null else ({
                    if (locating) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    else IconButton(onClick = { fetchLocation() }) {
                        Icon(Icons.Default.MyLocation, contentDescription = "Use my current location")
                    }
                }),
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

            Card(
                shape  = RoundedCornerShape(10.dp),
                colors = CardDefaults.cardColors(containerColor = if (isAdmin) MaterialTheme.colorScheme.surface else Color.White),
                elevation = CardDefaults.cardElevation(if (isAdmin) 1.dp else 0.dp),
                border = if (isAdmin) null else androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
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

            if (!isAdmin) {
                Card(
                    shape  = RoundedCornerShape(10.dp),
                    colors = CardDefaults.cardColors(containerColor = Color.White),
                    border = BorderStroke(
                        1.dp,
                        if (photoUri == null) MaterialTheme.colorScheme.error.copy(alpha = 0.5f)
                        else Color(0xFF16A34A).copy(alpha = 0.6f)
                    ),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(
                                if (photoUri == null) Icons.Default.PhotoCamera else Icons.Default.CheckCircle,
                                null,
                                tint = if (photoUri == null) MaterialTheme.colorScheme.error else Color(0xFF16A34A),
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(Modifier.width(6.dp))
                            Text(
                                if (photoUri == null) "Vehicle photo required *" else "Vehicle photo captured",
                                style = MaterialTheme.typography.labelMedium,
                                fontWeight = FontWeight.Bold,
                                color = if (photoUri == null) MaterialTheme.colorScheme.error else Color(0xFF16A34A)
                            )
                        }
                        if (photoUri != null) {
                            AsyncImage(
                                model = photoUri,
                                contentDescription = "Captured vehicle photo",
                                contentScale = ContentScale.Crop,
                                modifier = Modifier
                                    .fillMaxWidth().height(180.dp)
                                    .clip(RoundedCornerShape(8.dp))
                            )
                        }
                        OutlinedButton(
                            onClick = { cameraLauncher.launch(newPhotoTarget()) },
                            enabled = !sending,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Icon(Icons.Default.PhotoCamera, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(8.dp))
                            Text(if (photoUri == null) "Capture Vehicle Photo" else "Retake Photo")
                        }
                    }
                }
            }

            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(
                    onClick  = { if (isAdmin) sendWhatsApp() else confirmWithPhoto() },
                    enabled  = isAdmin || (photoUri != null && !sending),
                    modifier = Modifier.weight(1f).height(52.dp),
                    shape    = RoundedCornerShape(10.dp),
                    colors   = ButtonDefaults.buttonColors(
                        containerColor = Color(0xFF25D366),
                        contentColor   = Color.White)
                ) {
                    if (sending) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = Color.White)
                    } else {
                        Icon(Icons.Default.Chat, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(8.dp))
                        Text("WhatsApp", fontWeight = FontWeight.Bold)
                    }
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
private fun SummaryRow(
    label: String,
    value: String?,
    mono: Boolean = false,
    alwaysShow: Boolean = false
) {
    val blank = value.isNullOrBlank()
    if (blank && !alwaysShow) return
    val shown = if (blank) "—" else value!!
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(
            label,
            style    = MaterialTheme.typography.labelSmall,
            color    = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.35f)
        )
        Text(
            shown,
            style      = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Medium,
            fontFamily = if (mono) FontFamily.Monospace else FontFamily.Default,
            modifier   = Modifier.weight(0.65f)
        )
    }
}
