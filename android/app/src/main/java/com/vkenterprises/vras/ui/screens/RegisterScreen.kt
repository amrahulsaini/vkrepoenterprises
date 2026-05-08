package com.vkenterprises.vras.ui.screens

import android.net.Uri
import android.util.Base64
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthUiState
import com.vkenterprises.vras.viewmodel.AuthViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RegisterScreen(vm: AuthViewModel, nav: NavController) {
    val context = LocalContext.current
    val state by vm.state.collectAsState()

    var mobile   by remember { mutableStateOf("") }
    var name     by remember { mutableStateOf("") }
    var address  by remember { mutableStateOf("") }
    var pincode  by remember { mutableStateOf("") }
    var pfpUri   by remember { mutableStateOf<Uri?>(null) }
    var pfpB64   by remember { mutableStateOf<String?>(null) }
    var error    by remember { mutableStateOf("") }

    val imagePicker = rememberLauncherForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri ->
        pfpUri = uri
        uri?.let { u ->
            val bytes = context.contentResolver.openInputStream(u)?.readBytes()
            pfpB64 = bytes?.let { Base64.encodeToString(it, Base64.NO_WRAP) }
        }
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
                .verticalScroll(rememberScrollState())
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Profile picture picker
            Box(contentAlignment = Alignment.BottomEnd) {
                if (pfpUri != null) {
                    AsyncImage(
                        model = pfpUri, contentDescription = null,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.size(96.dp).clip(CircleShape)
                            .border(2.dp, MaterialTheme.colorScheme.primary, CircleShape)
                    )
                } else {
                    Box(
                        Modifier.size(96.dp).clip(CircleShape)
                            .background(MaterialTheme.colorScheme.primaryContainer)
                            .clickable { imagePicker.launch("image/*") },
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(Icons.Default.Person, null,
                            Modifier.size(48.dp),
                            tint = MaterialTheme.colorScheme.primary)
                    }
                }
                SmallFloatingActionButton(
                    onClick = { imagePicker.launch("image/*") },
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor   = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.size(28.dp)
                ) {
                    Icon(Icons.Default.CameraAlt, null, Modifier.size(16.dp))
                }
            }
            Text("Profile photo (optional)", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)

            OutlinedTextField(
                value = mobile, onValueChange = { mobile = it },
                label = { Text("Mobile Number *") },
                leadingIcon = { Icon(Icons.Default.Phone, null) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone),
                singleLine = true, modifier = Modifier.fillMaxWidth()
            )
            OutlinedTextField(
                value = name, onValueChange = { name = it },
                label = { Text("Full Name *") },
                leadingIcon = { Icon(Icons.Default.Person, null) },
                singleLine = true, modifier = Modifier.fillMaxWidth()
            )
            OutlinedTextField(
                value = address, onValueChange = { address = it },
                label = { Text("Address") },
                leadingIcon = { Icon(Icons.Default.Home, null) },
                maxLines = 3, modifier = Modifier.fillMaxWidth()
            )
            OutlinedTextField(
                value = pincode, onValueChange = { pincode = it.take(6) },
                label = { Text("Pincode") },
                leadingIcon = { Icon(Icons.Default.LocationOn, null) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, modifier = Modifier.fillMaxWidth()
            )

            if (error.isNotEmpty())
                Card(colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.errorContainer)) {
                    Text(error, Modifier.padding(12.dp),
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }

            Button(
                onClick = {
                    if (mobile.isBlank() || name.isBlank()) { error = "Mobile and name are required."; return@Button }
                    vm.register(mobile, name, address.ifBlank { null }, pincode.ifBlank { null }, pfpB64)
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

            TextButton(onClick = { nav.popBackStack() }) {
                Text("Already have an account? Login")
            }
        }
    }
}
