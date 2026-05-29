package com.vkenterprises.vras.ui.screens

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import com.vkenterprises.vras.utils.compressImageToBase64
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import androidx.compose.foundation.shape.*
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusEvent
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import androidx.compose.foundation.Image
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import coil.compose.AsyncImage
import com.vkenterprises.vras.BuildConfig
import com.vkenterprises.vras.R
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthUiState
import com.vkenterprises.vras.viewmodel.AuthViewModel
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class, ExperimentalFoundationApi::class)
@Composable
fun RegisterScreen(vm: AuthViewModel, nav: NavController) {
    val context      = LocalContext.current
    val state        by vm.state.collectAsState()
    val focusManager = LocalFocusManager.current
    val scope        = rememberCoroutineScope()
    val scrollState  = rememberScrollState()

    // Basic fields
    var mobile   by remember { mutableStateOf("") }
    var name     by remember { mutableStateOf("") }
    var address  by remember { mutableStateOf("") }
    var pincode  by remember { mutableStateOf("") }
    var pfpUri   by remember { mutableStateOf<Uri?>(null) }
    var pfpB64   by remember { mutableStateOf<String?>(null) }

    // KYC fields
    var aadhaarFrontUri by remember { mutableStateOf<Uri?>(null) }
    var aadhaarFrontB64 by remember { mutableStateOf<String?>(null) }
    var aadhaarBackUri  by remember { mutableStateOf<Uri?>(null) }
    var aadhaarBackB64  by remember { mutableStateOf<String?>(null) }
    var panFrontUri     by remember { mutableStateOf<Uri?>(null) }
    var panFrontB64     by remember { mutableStateOf<String?>(null) }
    var accountNumber   by remember { mutableStateOf("") }
    var ifscCode        by remember { mutableStateOf("") }

    var error by remember { mutableStateOf("") }

    // White-label build — agency is fixed at compile time. The agency
    // primary-mobile verification field was removed per UX request — the
    // server now accepts an empty agencyMobile and skips that check.
    val agencySlug = BuildConfig.AGENCY_SLUG
    val agencyName = BuildConfig.AGENCY_NAME

    // Compress every picked image down to <= 1280px max side + JPEG quality 80
    // before base64-encoding. Modern phone cameras output 4-8 MB photos; raw
    // base64 of that turns the register payload into 20-30 MB, which times
    // out reliably on slow networks. Post-compression we're at ~80-200 KB
    // per image — register call completes in 2-5 seconds even on 3G.
    fun uriToBase64(uri: Uri): String? = runCatching {
        compressImageToBase64(context, uri)
    }.getOrNull()

    val pfpPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        pfpUri = uri; pfpB64 = uri?.let { uriToBase64(it) }
    }
    val aadhaarFrontPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        aadhaarFrontUri = uri; aadhaarFrontB64 = uri?.let { uriToBase64(it) }
    }
    val aadhaarBackPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        aadhaarBackUri = uri; aadhaarBackB64 = uri?.let { uriToBase64(it) }
    }
    val panFrontPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        panFrontUri = uri; panFrontB64 = uri?.let { uriToBase64(it) }
    }

    LaunchedEffect(state) {
        when (state) {
            is AuthUiState.RegisterSuccess -> {
                vm.resetState()
                nav.navigate(Screen.WaitingApproval.go("registered")) {
                    popUpTo(Screen.Register.route) { inclusive = true }
                }
            }
            is AuthUiState.Error -> { error = (state as AuthUiState.Error).message; vm.resetState() }
            else -> {}
        }
    }

    // Full-screen upload progress dialog. Replaces the tiny inline spinner
    // with a clear "we're uploading" message so the user doesn't think the
    // app froze during the multi-MB POST.
    if (state is AuthUiState.Loading) {
        UploadingDialog(
            hasImages = listOf(pfpB64, aadhaarFrontB64, aadhaarBackB64, panFrontB64).any { it != null },
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Create Account") },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, null)
                    }
                }
            )
        }
    ) { pad ->
        Column(
            Modifier
                .padding(pad)
                .fillMaxSize()
                .imePadding()
                .verticalScroll(scrollState)
                .padding(horizontal = 24.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {

            // ── Profile photo ──────────────────────────────────────────
            Box(Modifier.align(Alignment.CenterHorizontally), contentAlignment = Alignment.BottomEnd) {
                if (pfpUri != null) {
                    AsyncImage(
                        model = pfpUri, contentDescription = null,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.size(88.dp).clip(CircleShape)
                            .border(2.dp, MaterialTheme.colorScheme.primary, CircleShape)
                            .clickable { pfpPicker.launch("image/*") }
                    )
                } else {
                    Box(
                        Modifier.size(88.dp).clip(CircleShape)
                            .background(MaterialTheme.colorScheme.primaryContainer)
                            .clickable { pfpPicker.launch("image/*") },
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(Icons.Default.Person, null, Modifier.size(44.dp),
                            tint = MaterialTheme.colorScheme.primary)
                    }
                }
                SmallFloatingActionButton(
                    onClick = { pfpPicker.launch("image/*") },
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor   = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.size(26.dp)
                ) { Icon(Icons.Default.CameraAlt, null, Modifier.size(14.dp)) }
            }
            Text(
                "Profile photo (optional)",
                Modifier.align(Alignment.CenterHorizontally),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            // ── Agency identity card — big agency logo + name. No
            // verification mobile field; agency identity is fixed by the
            // per-flavor build (BuildConfig.AGENCY_SLUG), so there's nothing
            // for the user to "confirm" any more.
            Surface(
                shape = RoundedCornerShape(14.dp),
                color = Color.White,
                shadowElevation = 1.dp,
                modifier = Modifier.fillMaxWidth().align(Alignment.CenterHorizontally)
            ) {
                Column(
                    Modifier.padding(vertical = 16.dp, horizontal = 12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Image(
                        painter = painterResource(id = R.drawable.agency_logo),
                        contentDescription = agencyName,
                        contentScale = ContentScale.Fit,
                        modifier = Modifier.size(72.dp).clip(RoundedCornerShape(12.dp))
                    )
                    Spacer(Modifier.height(8.dp))
                    Text(agencyName,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface)
                }
            }

            // ── Basic info ─────────────────────────────────────────────
            SectionHeader("Basic Information")

            FocusedField(scrollState) {
                OutlinedTextField(
                    value = mobile, onValueChange = { mobile = it },
                    label = { Text("Mobile Number *") },
                    leadingIcon = { Icon(Icons.Default.Phone, null) },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone, imeAction = ImeAction.Next),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }
            FocusedField(scrollState) {
                OutlinedTextField(
                    value = name, onValueChange = { name = it },
                    label = { Text("Full Name *") },
                    leadingIcon = { Icon(Icons.Default.Person, null) },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }
            FocusedField(scrollState) {
                OutlinedTextField(
                    value = address, onValueChange = { address = it },
                    label = { Text("Address") },
                    leadingIcon = { Icon(Icons.Default.Home, null) },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                    maxLines = 3, modifier = Modifier.fillMaxWidth().then(it)
                )
            }
            FocusedField(scrollState) {
                OutlinedTextField(
                    value = pincode, onValueChange = { pincode = it.take(6) },
                    label = { Text("Pincode") },
                    leadingIcon = { Icon(Icons.Default.LocationOn, null) },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }

            // ── Bank details ───────────────────────────────────────────
            SectionHeader("Bank Details")

            FocusedField(scrollState) {
                OutlinedTextField(
                    value = accountNumber, onValueChange = { accountNumber = it },
                    label = { Text("Account Number") },
                    leadingIcon = { Icon(Icons.Default.AccountBalance, null) },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }
            FocusedField(scrollState) {
                OutlinedTextField(
                    value = ifscCode, onValueChange = { ifscCode = it.uppercase().take(11) },
                    label = { Text("IFSC Code") },
                    leadingIcon = { Icon(Icons.Default.Code, null) },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { focusManager.clearFocus() }),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }

            // ── KYC documents ──────────────────────────────────────────
            SectionHeader("KYC Documents")
            Text(
                "Upload Aadhaar (front & back) and PAN card front image.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                KycImageCard(
                    label = "Aadhaar Front",
                    uri = aadhaarFrontUri,
                    modifier = Modifier.weight(1f),
                    onClick = { aadhaarFrontPicker.launch("image/*") }
                )
                KycImageCard(
                    label = "Aadhaar Back",
                    uri = aadhaarBackUri,
                    modifier = Modifier.weight(1f),
                    onClick = { aadhaarBackPicker.launch("image/*") }
                )
            }
            KycImageCard(
                label = "PAN Card Front",
                uri = panFrontUri,
                modifier = Modifier.fillMaxWidth().height(110.dp),
                onClick = { panFrontPicker.launch("image/*") }
            )

            // ── Error ──────────────────────────────────────────────────
            if (error.isNotEmpty()) {
                Card(colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.errorContainer)) {
                    Text(error, Modifier.padding(12.dp),
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }
            }

            // ── Submit ─────────────────────────────────────────────────
            Button(
                onClick = {
                    focusManager.clearFocus()
                    // All fields are now mandatory — profile, full KYC and bank
                    // details must be provided before an agent can register.
                    error = when {
                        mobile.isBlank() || name.isBlank() -> "Mobile and name are required."
                        address.isBlank()                  -> "Address is required."
                        pincode.isBlank()                  -> "Pincode is required."
                        pfpB64 == null                     -> "Profile photo is required."
                        aadhaarFrontB64 == null            -> "Aadhaar front photo is required."
                        aadhaarBackB64 == null             -> "Aadhaar back photo is required."
                        panFrontB64 == null                -> "PAN card photo is required."
                        accountNumber.isBlank()            -> "Bank account number is required."
                        ifscCode.isBlank()                 -> "IFSC code is required."
                        else                               -> ""
                    }
                    if (error.isNotEmpty()) return@Button
                    vm.register(
                        mobile, name,
                        address, pincode,
                        pfpB64,
                        aadhaarFrontB64, aadhaarBackB64, panFrontB64,
                        accountNumber, ifscCode,
                        agencySlug, agencyName, ""  // agencyMobile no longer collected
                    )
                },
                enabled  = state !is AuthUiState.Loading,
                modifier = Modifier.fillMaxWidth().height(52.dp)
            ) {
                if (state is AuthUiState.Loading)
                    CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary)
                else
                    Text("REGISTER", fontWeight = FontWeight.Bold, fontSize = 16.sp)
            }

            TextButton(
                onClick = { nav.popBackStack() },
                modifier = Modifier.align(Alignment.CenterHorizontally)
            ) {
                Text("Already have an account? Login")
            }

            Spacer(Modifier.height(16.dp))
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun FocusedField(
    scrollState: ScrollState,
    content: @Composable (Modifier) -> Unit
) {
    val requester = remember { BringIntoViewRequester() }
    val scope     = rememberCoroutineScope()
    content(
        Modifier.bringIntoViewRequester(requester).onFocusEvent { fs ->
            if (fs.isFocused) scope.launch { requester.bringIntoView() }
        }
    )
}

@Composable
private fun SectionHeader(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleSmall,
        fontWeight = FontWeight.Bold,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 6.dp)
    )
    HorizontalDivider(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.3f))
}

@Composable
private fun KycImageCard(
    label: String,
    uri: Uri?,
    modifier: Modifier = Modifier,
    onClick: () -> Unit
) {
    OutlinedCard(
        onClick = onClick,
        modifier = modifier.defaultMinSize(minHeight = 110.dp)
    ) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            if (uri != null) {
                AsyncImage(
                    model = uri, contentDescription = label,
                    contentScale = ContentScale.Crop,
                    modifier = Modifier.fillMaxSize()
                )
                // Label overlay
                Box(
                    Modifier.fillMaxWidth().align(Alignment.BottomCenter)
                        .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.75f))
                        .padding(4.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(label, style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurface)
                }
            } else {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                    modifier = Modifier.padding(12.dp)
                ) {
                    Icon(Icons.Default.AddAPhoto, null,
                        Modifier.size(28.dp),
                        tint = MaterialTheme.colorScheme.primary)
                    Text(label,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center)
                }
            }
        }
    }
}

// Full-screen non-dismissible dialog while the register call is in flight.
// Three status lines cycle every ~2s so the user sees there's real activity:
// "Compressing photos…" → "Uploading to server…" → "Almost done…". The
// indeterminate progress bar shows the call is still healthy.
@Composable
private fun UploadingDialog(hasImages: Boolean) {
    var step by remember { mutableStateOf(0) }
    val steps = if (hasImages) listOf(
        "Compressing your photos…",
        "Uploading documents to server…",
        "Almost done — verifying your details…"
    ) else listOf(
        "Sending your details to server…",
        "Almost done — verifying…",
        "Just a moment…"
    )
    LaunchedEffect(Unit) {
        while (true) {
            kotlinx.coroutines.delay(2200)
            step = (step + 1) % steps.size
        }
    }
    AlertDialog(
        onDismissRequest = { /* not dismissible — registration is in flight */ },
        confirmButton = { },
        title = {
            Text("Creating your account",
                fontWeight = FontWeight.Bold,
                style = MaterialTheme.typography.titleMedium)
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                LinearProgressIndicator(
                    modifier = Modifier.fillMaxWidth(),
                    color = MaterialTheme.colorScheme.primary,
                    trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.15f)
                )
                Text(steps[step],
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurface)
                Text("Please keep the app open. This usually takes 5–15 seconds depending on your network.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    )
}

// compressImageToBase64 lives in com.vkenterprises.vras.utils.ImageUpload.kt
// — shared with ProfileScreen so the pfp picker uses the same compression.
