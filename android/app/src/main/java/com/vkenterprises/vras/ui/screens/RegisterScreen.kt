package com.vkenterprises.vras.ui.screens

import android.net.Uri
import android.util.Base64
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
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
import coil.compose.AsyncImage
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

    fun uriToBase64(uri: Uri): String? = runCatching {
        val bytes = context.contentResolver.openInputStream(uri)?.readBytes()
        bytes?.let { Base64.encodeToString(it, Base64.NO_WRAP) }
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
                    if (mobile.isBlank() || name.isBlank()) {
                        error = "Mobile and name are required."
                        return@Button
                    }
                    vm.register(
                        mobile, name,
                        address.ifBlank { null }, pincode.ifBlank { null },
                        pfpB64,
                        aadhaarFrontB64, aadhaarBackB64, panFrontB64,
                        accountNumber.ifBlank { null }, ifscCode.ifBlank { null }
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
