package com.vkenterprises.vras.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthUiState
import com.vkenterprises.vras.viewmodel.AuthViewModel

@Composable
fun LoginScreen(vm: AuthViewModel, nav: NavController) {
    val state by vm.state.collectAsState()

    var mobile by remember { mutableStateOf("") }
    var error  by remember { mutableStateOf("") }

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

            // Logo area
            Surface(
                shape = RoundedCornerShape(24.dp),
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(80.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(
                        "VK",
                        color = MaterialTheme.colorScheme.onPrimary,
                        fontSize = 32.sp,
                        fontWeight = FontWeight.Black
                    )
                }
            }

            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    "Welcome Back",
                    style = MaterialTheme.typography.headlineMedium,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    "Sign in to VK Enterprises",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            Spacer(Modifier.height(8.dp))

            OutlinedTextField(
                value = mobile,
                onValueChange = { mobile = it.take(15) },
                label = { Text("Mobile Number") },
                leadingIcon = { Icon(Icons.Default.Phone, null) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone),
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(12.dp)
            )

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
                    if (mobile.isBlank()) { error = "Enter your mobile number."; return@Button }
                    vm.login(mobile)
                },
                enabled = state !is AuthUiState.Loading,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(12.dp)
            ) {
                if (state is AuthUiState.Loading)
                    CircularProgressIndicator(
                        Modifier.size(20.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                else
                    Text("LOGIN", fontWeight = FontWeight.Bold, fontSize = 16.sp)
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
