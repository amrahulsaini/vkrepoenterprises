package com.vkenterprises.vras.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.*
import androidx.compose.foundation.Image
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.navigation.NavController
import com.vkenterprises.vras.BuildConfig
import com.vkenterprises.vras.R
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthUiState
import com.vkenterprises.vras.viewmodel.AuthViewModel

@Composable
fun LoginScreen(vm: AuthViewModel, nav: NavController) {
    val state by vm.state.collectAsState()

    var mobile  by remember { mutableStateOf("") }
    var error   by remember { mutableStateOf("") }
    // OTP step state: once the SMS goes out we swap the button to "Verify & Login".
    var otpSent by remember { mutableStateOf(false) }
    var otp     by remember { mutableStateOf("") }
    var busy    by remember { mutableStateOf(false) }
    // White-label build — agency is baked into BuildConfig at compile time.
    val agencySlug = BuildConfig.AGENCY_SLUG
    val agencyName = BuildConfig.AGENCY_NAME

    LaunchedEffect(state) {
        when (state) {
            is AuthUiState.LoginSuccess -> {
                vm.resetState()
                nav.navigate(Screen.Home.route) {
                    popUpTo(Screen.Login.route) { inclusive = true }
                }
            }
            is AuthUiState.SubscriptionExpired -> {
                vm.resetState()
                nav.navigate(Screen.SubscriptionExpired.route) {
                    popUpTo(Screen.Login.route) { inclusive = true }
                }
            }
            is AuthUiState.PendingApproval -> {
                vm.resetState()
                nav.navigate(Screen.WaitingApproval.go("pending_approval"))
            }
            is AuthUiState.DeviceMismatch -> {
                vm.resetState()
                nav.navigate(Screen.WaitingApproval.go("device_mismatch"))
            }
            is AuthUiState.AppStopped -> {
                vm.resetState()
                nav.navigate(Screen.AppStopped.route) {
                    popUpTo(Screen.Login.route) { inclusive = true }
                }
            }
            is AuthUiState.Blacklisted -> {
                vm.resetState()
                nav.navigate(Screen.Blacklisted.route) {
                    popUpTo(Screen.Login.route) { inclusive = true }
                }
            }
            is AuthUiState.Inactive -> {
                vm.resetState()
                nav.navigate(Screen.Inactive.route) {
                    popUpTo(Screen.Login.route) { inclusive = true }
                }
            }
            is AuthUiState.KycPending -> {
                vm.resetState()
                nav.navigate(Screen.KycPending.route)
            }
            is AuthUiState.KycRejected -> {
                vm.resetState()
                nav.navigate(Screen.KycRejected.route)
            }
            is AuthUiState.Error -> {
                error = (state as AuthUiState.Error).message
                vm.resetState()
            }
            else -> {}
        }
    }

    Box(
        Modifier.fillMaxSize(),
        contentAlignment = Alignment.Center
    ) {
        Column(
            Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            Spacer(Modifier.height(40.dp))

            // CRMS logo — bundled as a drawable, white background per brand guide.
            Surface(
                shape = RoundedCornerShape(20.dp),
                color = Color.White,
                shadowElevation = 2.dp,
                modifier = Modifier.size(112.dp)
            ) {
                Image(
                    painter = painterResource(id = R.drawable.agency_logo),
                    contentDescription = agencyName,
                    contentScale = ContentScale.Fit,
                    modifier = Modifier.fillMaxSize().padding(10.dp)
                )
            }

            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    "Welcome Back",
                    style = MaterialTheme.typography.headlineMedium,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    "Sign in to $agencyName",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            Spacer(Modifier.height(8.dp))

            OutlinedTextField(
                value = mobile,
                onValueChange = { if (!otpSent) mobile = it.take(15) },
                label = { Text("Mobile Number") },
                leadingIcon = { Icon(Icons.Default.Phone, null) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone),
                singleLine = true,
                enabled = !otpSent && !busy,
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(12.dp)
            )

            if (otpSent) {
                OutlinedTextField(
                    value = otp,
                    onValueChange = { otp = it.filter { c -> c.isDigit() }.take(6) },
                    label = { Text("Enter OTP") },
                    leadingIcon = { Icon(Icons.Default.Sms, null) },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(12.dp)
                )
                Text(
                    "We sent a 6-digit code to $mobile",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            if (error.isNotEmpty()) {
                Card(
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer
                    ),
                    shape = RoundedCornerShape(10.dp)
                ) {
                    Text(
                        error,
                        Modifier.padding(12.dp),
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        style = MaterialTheme.typography.bodySmall
                    )
                }
            }

            Button(
                onClick = {
                    error = ""
                    if (!otpSent) {
                        if (mobile.isBlank()) { error = "Enter your mobile number."; return@Button }
                        busy = true
                        vm.sendOtp(mobile) { ok, msg ->
                            busy = false
                            if (ok) otpSent = true else error = msg
                        }
                    } else {
                        if (otp.length < 4) { error = "Enter the OTP sent to your phone."; return@Button }
                        busy = true
                        vm.verifyOtp(mobile, otp) { ok, msg ->
                            busy = false
                            if (ok) vm.login(mobile, agencySlug, agencyName) else error = msg
                        }
                    }
                },
                enabled = !busy && state !is AuthUiState.Loading,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(12.dp)
            ) {
                if (busy || state is AuthUiState.Loading)
                    CircularProgressIndicator(
                        Modifier.size(20.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                else
                    Text(if (otpSent) "VERIFY & LOGIN" else "SEND OTP",
                        fontWeight = FontWeight.Bold, fontSize = 16.sp)
            }

            if (otpSent) {
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    TextButton(onClick = { otpSent = false; otp = ""; error = "" }, enabled = !busy) {
                        Text("Change number")
                    }
                    TextButton(onClick = {
                        error = ""; busy = true
                        vm.sendOtp(mobile) { ok, msg -> busy = false; if (!ok) error = msg }
                    }, enabled = !busy) {
                        Text("Resend OTP")
                    }
                }
            }

            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.Center
            ) {
                HorizontalDivider(Modifier.weight(1f))
                Text(
                    "  OR  ",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                HorizontalDivider(Modifier.weight(1f))
            }

            OutlinedButton(
                onClick = { nav.navigate(Screen.Register.route) },
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(12.dp)
            ) {
                Text("CREATE ACCOUNT", fontWeight = FontWeight.SemiBold, fontSize = 15.sp)
            }

            Text(
                "Enter your registered mobile number to log in.\nNo password required.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center
            )

            Spacer(Modifier.height(40.dp))
        }
    }
}
