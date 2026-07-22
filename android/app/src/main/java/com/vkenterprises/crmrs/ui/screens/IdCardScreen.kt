package com.vkenterprises.crmrs.ui.screens

import android.app.Activity
import android.net.Uri
import android.view.WindowManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
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
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.R
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.IdCardResponse
import com.vkenterprises.crmrs.data.models.IdCardSubmitRequest
import com.vkenterprises.crmrs.utils.compressImageToBase64
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private val BLOOD_GROUPS = listOf("A+", "A-", "B+", "B-", "O+", "O-", "AB+", "AB-")
private val BRAND = Color(0xFF1565C0)
private val OK_GREEN = Color(0xFF16A34A)
private val ERR_RED = Color(0xFFDC2626)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun IdCardScreen(vm: AuthViewModel, nav: NavController) {
    val context = LocalContext.current
    val scope   = rememberCoroutineScope()
    val userName by vm.userName.collectAsState(initial = "")
    val userMobile by vm.userMobile.collectAsState(initial = "")
    val userId by vm.userId.collectAsState(initial = -1L)

    // Screenshot / screen-record protection for this whole screen.
    DisposableEffect(Unit) {
        val window = (context as? Activity)?.window
        window?.setFlags(WindowManager.LayoutParams.FLAG_SECURE, WindowManager.LayoutParams.FLAG_SECURE)
        onDispose { window?.clearFlags(WindowManager.LayoutParams.FLAG_SECURE) }
    }

    var loading by remember { mutableStateOf(true) }
    var card    by remember { mutableStateOf<IdCardResponse?>(null) }

    suspend fun reload() {
        loading = true
        runCatching {
            val uid = vm.userId.first()
            val r = ApiClient.api.getIdCard(uid)
            if (r.isSuccessful) card = r.body()
        }
        loading = false
    }
    LaunchedEffect(Unit) { reload() }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("ID Card", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                }
            )
        }
    ) { pad ->
        Box(Modifier.padding(pad).fillMaxSize()) {
            when {
                loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
                card?.status == "approved" && card?.expired == false ->
                    ApprovedCard(card!!, userName, userMobile)
                card?.status == "approved" && card?.expired == true ->
                    ExpiredState()
                card?.status == "pending" ->
                    PendingState()
                else ->  // "none" or "declined" -> submit / re-submit form
                    SubmitForm(
                        userId        = userId,
                        declineReason = card?.takeIf { it.status == "declined" }?.declineReason,
                        prefillBlood  = card?.bloodGroup,
                        prefillDob    = card?.dob,
                        onSubmitted   = { scope.launch { reload() } }
                    )
            }
        }
    }
}

@Composable
private fun ApprovedCard(card: IdCardResponse, name: String, mobile: String) {
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 8.dp)) {
            Icon(Icons.Default.Lock, null, tint = OK_GREEN, modifier = Modifier.size(14.dp))
            Spacer(Modifier.width(4.dp))
            Text("Protected — screenshots are disabled",
                style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }

        Surface(
            shape = RoundedCornerShape(18.dp),
            shadowElevation = 8.dp,
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(Modifier.background(Color.White)) {
                // Header band with agency identity
                Row(
                    Modifier.fillMaxWidth()
                        .background(Brush.horizontalGradient(listOf(BRAND, Color(0xFF0D47A1))))
                        .padding(14.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Image(
                        painter = painterResource(id = R.drawable.agency_logo),
                        contentDescription = null,
                        modifier = Modifier.size(40.dp).clip(RoundedCornerShape(8.dp))
                    )
                    Spacer(Modifier.width(10.dp))
                    Column(Modifier.weight(1f)) {
                        Text(BuildConfig.AGENCY_NAME, color = Color.White,
                            fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium, maxLines = 1)
                        Text("OFFICIAL AGENT ID CARD", color = Color.White.copy(alpha = 0.85f),
                            style = MaterialTheme.typography.labelSmall, letterSpacing = 1.5.sp)
                    }
                }

                Row(Modifier.padding(16.dp)) {
                    AsyncImage(
                        model = card.photoUrl,
                        contentDescription = "Photo",
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.size(96.dp)
                            .clip(RoundedCornerShape(10.dp))
                            .background(MaterialTheme.colorScheme.surfaceVariant)
                    )
                    Spacer(Modifier.width(16.dp))
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(name.ifBlank { "—" }, fontWeight = FontWeight.Bold,
                            style = MaterialTheme.typography.titleMedium, color = Color(0xFF111111))
                        IdField("Blood Group", card.bloodGroup ?: "—")
                        IdField("Date of Birth", card.dob ?: "—")
                        if (mobile.isNotBlank()) IdField("Mobile", mobile)
                    }
                }

                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                Row(
                    Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Default.Verified, null, tint = OK_GREEN, modifier = Modifier.size(16.dp))
                        Spacer(Modifier.width(4.dp))
                        Text("Verified by ${BuildConfig.AGENCY_NAME}",
                            style = MaterialTheme.typography.labelSmall, color = OK_GREEN, fontWeight = FontWeight.SemiBold)
                    }
                    Text("Valid until ${card.validUntil ?: "—"}",
                        style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
        Spacer(Modifier.height(16.dp))
        Text("Show this card to the customer for verification. It expires on ${card.validUntil ?: "—"}.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
    }
}

@Composable
private fun IdField(label: String, value: String) {
    Row {
        Text("$label: ", style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value, style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.SemiBold, color = Color(0xFF111111))
    }
}

@Composable
private fun PendingState() = CenterMessage(
    icon = Icons.Default.HourglassTop, tint = Color(0xFFB45309),
    title = "Awaiting Admin Approval",
    body  = "Your documents have been submitted. Your ID card will be issued once the Admin Department approves them."
)

@Composable
private fun ExpiredState() = CenterMessage(
    icon = Icons.Default.EventBusy, tint = ERR_RED,
    title = "ID Card Expired",
    body  = "Your ID card validity has ended. Please contact the Admin Department to renew and re-approve it."
)

@Composable
private fun CenterMessage(icon: androidx.compose.ui.graphics.vector.ImageVector, tint: Color, title: String, body: String) {
    Box(Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Surface(shape = RoundedCornerShape(50), color = tint.copy(alpha = 0.12f), modifier = Modifier.size(88.dp)) {
                Box(contentAlignment = Alignment.Center) { Icon(icon, null, tint = tint, modifier = Modifier.size(44.dp)) }
            }
            Text(title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
            Text(body, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Center,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SubmitForm(
    userId: Long,
    declineReason: String?,
    prefillBlood: String?,
    prefillDob: String?,
    onSubmitted: () -> Unit
) {
    val context = LocalContext.current
    val scope   = rememberCoroutineScope()

    var photoUri by remember { mutableStateOf<Uri?>(null) }
    var pccUri   by remember { mutableStateOf<Uri?>(null) }
    var draUri   by remember { mutableStateOf<Uri?>(null) }
    var blood    by remember { mutableStateOf(prefillBlood ?: "") }
    var dob      by remember { mutableStateOf(prefillDob ?: "") }
    var bloodExpanded by remember { mutableStateOf(false) }
    var showDate by remember { mutableStateOf(false) }
    var submitting by remember { mutableStateOf(false) }
    var error    by remember { mutableStateOf("") }

    val photoPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { photoUri = it }
    val pccPicker   = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { pccUri = it }
    val draPicker   = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { draUri = it }

    if (showDate) {
        val state = rememberDatePickerState()
        DatePickerDialog(
            onDismissRequest = { showDate = false },
            confirmButton = {
                TextButton(onClick = {
                    state.selectedDateMillis?.let {
                        dob = java.time.Instant.ofEpochMilli(it)
                            .atZone(java.time.ZoneOffset.UTC).toLocalDate().toString()
                    }
                    showDate = false
                }) { Text("OK") }
            },
            dismissButton = { TextButton(onClick = { showDate = false }) { Text("Cancel") } }
        ) { DatePicker(state = state) }
    }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        if (!declineReason.isNullOrBlank()) {
            Card(colors = CardDefaults.cardColors(containerColor = ERR_RED.copy(alpha = 0.08f)),
                border = BorderStroke(1.dp, ERR_RED.copy(alpha = 0.4f)), shape = RoundedCornerShape(10.dp)) {
                Column(Modifier.padding(12.dp)) {
                    Text("Your ID card request was declined", fontWeight = FontWeight.Bold, color = ERR_RED,
                        style = MaterialTheme.typography.labelMedium)
                    Spacer(Modifier.height(4.dp))
                    Text(declineReason, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurface)
                    Spacer(Modifier.height(4.dp))
                    Text("Please re-upload the documents below.", style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        Text("Generate your official ID Card", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        Text(
            "Submit the following documents to the Admin Department. Once approved, your ID card is issued " +
                "(validity is set by the admin, e.g. 1 day, and renewed as per process).",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant
        )

        DocUpload("1. Passport Size Photograph", photoUri) { photoPicker.launch("image/*") }
        DocUpload("2. Police Clearance Certificate (PCC) — issued within last 3 months", pccUri) { pccPicker.launch("image/*") }
        DocUpload("3. DRA Certificate", draUri) { draPicker.launch("image/*") }

        // Blood group
        ExposedDropdownMenuBox(expanded = bloodExpanded, onExpandedChange = { bloodExpanded = it }) {
            OutlinedTextField(
                value = blood, onValueChange = {}, readOnly = true,
                label = { Text("4. Blood Group *") },
                leadingIcon = { Icon(Icons.Default.Bloodtype, null) },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(bloodExpanded) },
                modifier = Modifier.fillMaxWidth().menuAnchor(), shape = RoundedCornerShape(10.dp)
            )
            ExposedDropdownMenu(expanded = bloodExpanded, onDismissRequest = { bloodExpanded = false }) {
                BLOOD_GROUPS.forEach { bg ->
                    DropdownMenuItem(text = { Text(bg) }, onClick = { blood = bg; bloodExpanded = false })
                }
            }
        }

        OutlinedTextField(
            value = dob, onValueChange = {}, readOnly = true,
            label = { Text("5. Date of Birth *") },
            leadingIcon = { Icon(Icons.Default.Cake, null) },
            trailingIcon = { IconButton(onClick = { showDate = true }) { Icon(Icons.Default.DateRange, null) } },
            modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(10.dp)
        )

        if (error.isNotEmpty()) {
            Text(error, color = ERR_RED, style = MaterialTheme.typography.bodySmall)
        }

        Button(
            onClick = {
                // On a first submission all three docs are required. On a decline
                // re-submit the server keeps any previously-stored image, so a
                // doc the user doesn't re-pick is allowed to stay as-is.
                error = when {
                    photoUri == null && declineReason == null -> "Passport size photograph is required."
                    pccUri == null   && declineReason == null -> "PCC document is required."
                    draUri == null   && declineReason == null -> "DRA certificate is required."
                    blood.isBlank()                           -> "Please select your blood group."
                    dob.isBlank()                             -> "Please select your date of birth."
                    else -> ""
                }
                if (error.isNotEmpty()) return@Button
                submitting = true
                scope.launch {
                    val ok = runCatching {
                        val photoB = photoUri?.let { withContext(Dispatchers.IO) { compressImageToBase64(context, it) } }
                        val pccB   = pccUri?.let   { withContext(Dispatchers.IO) { compressImageToBase64(context, it) } }
                        val draB   = draUri?.let   { withContext(Dispatchers.IO) { compressImageToBase64(context, it) } }
                        val r = ApiClient.api.submitIdCard(
                            userId,
                            IdCardSubmitRequest(
                                photoBase64 = photoB,
                                pccBase64   = pccB,
                                draBase64   = draB,
                                bloodGroup  = blood,
                                dob         = dob
                            )
                        )
                        r.isSuccessful
                    }.getOrDefault(false)
                    submitting = false
                    if (ok) onSubmitted()
                    else error = "Could not submit. Please check your connection and try again."
                }
            },
            enabled = !submitting,
            modifier = Modifier.fillMaxWidth().height(52.dp), shape = RoundedCornerShape(10.dp),
            colors = ButtonDefaults.buttonColors(containerColor = BRAND, contentColor = Color.White)
        ) {
            if (submitting) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp, color = Color.White)
            else Text("SUBMIT FOR APPROVAL", fontWeight = FontWeight.Bold)
        }
        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun DocUpload(label: String, uri: Uri?, onPick: () -> Unit) {
    OutlinedCard(onClick = onPick, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(10.dp)) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            if (uri != null) {
                AsyncImage(model = uri, contentDescription = null, contentScale = ContentScale.Crop,
                    modifier = Modifier.size(52.dp).clip(RoundedCornerShape(8.dp)))
            } else {
                Box(Modifier.size(52.dp).clip(RoundedCornerShape(8.dp))
                    .background(MaterialTheme.colorScheme.surfaceVariant), contentAlignment = Alignment.Center) {
                    Icon(Icons.Default.UploadFile, null, tint = MaterialTheme.colorScheme.primary)
                }
            }
            Spacer(Modifier.width(12.dp))
            Text(label, style = MaterialTheme.typography.bodySmall, modifier = Modifier.weight(1f))
            Icon(if (uri != null) Icons.Default.CheckCircle else Icons.Default.ChevronRight,
                null, tint = if (uri != null) OK_GREEN else MaterialTheme.colorScheme.outline)
        }
    }
}
