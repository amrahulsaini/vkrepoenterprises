package com.vkenterprises.crmrs.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.navigation.Screen
import com.vkenterprises.crmrs.viewmodel.AuthUiState
import com.vkenterprises.crmrs.viewmodel.AuthViewModel

private val OK_GREEN = Color(0xFF16A34A)
private val WARN_AMBER = Color(0xFFB45309)
private val ERR_RED = Color(0xFFDC2626)

@Composable
internal fun StatusActions(vm: AuthViewModel, nav: NavController) {
    val state by vm.state.collectAsState()
    LaunchedEffect(state) { routeAuthState(state, nav, vm) }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Button(
            onClick = { vm.login(vm.lastMobile, BuildConfig.AGENCY_SLUG, BuildConfig.AGENCY_NAME) },
            enabled = state !is AuthUiState.Loading,
            modifier = Modifier.fillMaxWidth().height(50.dp),
            shape = RoundedCornerShape(12.dp)
        ) {
            if (state is AuthUiState.Loading)
                CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp, color = MaterialTheme.colorScheme.onPrimary)
            else {
                Icon(Icons.Default.Refresh, null, Modifier.size(18.dp)); Spacer(Modifier.width(8.dp))
                Text("CHECK AGAIN", fontWeight = FontWeight.Bold, fontSize = 15.sp)
            }
        }
        TextButton(onClick = { nav.navigate(Screen.Login.route) { popUpTo(0) { inclusive = true } } },
            modifier = Modifier.fillMaxWidth()) { Text("Back to login") }
    }
}

private fun routeAuthState(state: AuthUiState, nav: NavController, vm: AuthViewModel) {
    when (state) {
        is AuthUiState.LoginSuccess -> {
            vm.resetState(); nav.navigate(Screen.Home.route) { popUpTo(0) { inclusive = true } }
        }
        is AuthUiState.SubscriptionExpired -> {
            vm.resetState(); nav.navigate(Screen.SubscriptionExpired.route) { popUpTo(0) { inclusive = true } }
        }
        is AuthUiState.PendingApproval -> {
            vm.resetState(); nav.navigate(Screen.WaitingApproval.go("pending_approval")) { popUpTo(0) { inclusive = true } }
        }
        is AuthUiState.AppStopped -> {
            vm.resetState(); nav.navigate(Screen.AppStopped.route) { popUpTo(0) { inclusive = true } }
        }
        is AuthUiState.Blacklisted -> {
            vm.resetState(); nav.navigate(Screen.Blacklisted.route) { popUpTo(0) { inclusive = true } }
        }
        is AuthUiState.Inactive -> {
            vm.resetState(); nav.navigate(Screen.Inactive.route) { popUpTo(0) { inclusive = true } }
        }
        is AuthUiState.KycPending -> {
            vm.resetState(); nav.navigate(Screen.KycPending.route) { popUpTo(0) { inclusive = true } }
        }
        is AuthUiState.KycRejected -> {
            vm.resetState(); nav.navigate(Screen.KycRejected.route) { popUpTo(0) { inclusive = true } }
        }
        else -> {}
    }
}

@Composable
fun KycPendingScreen(vm: AuthViewModel, nav: NavController) {
    val state by vm.state.collectAsState()
    LaunchedEffect(state) { routeAuthState(state, nav, vm) }
    KycStatusBody(
        icon = Icons.Default.HourglassTop,
        tint = WARN_AMBER,
        title = "KYC Under Review",
        message = vm.lastKycMessage.ifBlank {
            "Your documents have been submitted. The agency is verifying your Aadhaar, " +
                "photos and details. You'll be able to log in once they approve."
        },
        loading = state is AuthUiState.Loading,
        primaryLabel = "Check again",
        onPrimary = { vm.login(vm.lastMobile, BuildConfig.AGENCY_SLUG, BuildConfig.AGENCY_NAME) },
        secondaryLabel = "Back to login",
        onSecondary = { nav.navigate(Screen.Login.route) { popUpTo(0) { inclusive = true } } }
    )
}

@Composable
fun KycRejectedScreen(vm: AuthViewModel, nav: NavController) {
    val state by vm.state.collectAsState()
    LaunchedEffect(state) { routeAuthState(state, nav, vm) }
    KycStatusBody(
        icon = Icons.Default.ErrorOutline,
        tint = ERR_RED,
        title = "KYC Rejected",
        message = vm.lastKycMessage.ifBlank {
            "Your KYC was rejected by the agency. Please re-submit your documents."
        },
        loading = state is AuthUiState.Loading,
        primaryLabel = "Re-submit KYC",
        onPrimary = { nav.navigate(Screen.KycResubmit.route) },
        secondaryLabel = "Back to login",
        onSecondary = { nav.navigate(Screen.Login.route) { popUpTo(0) { inclusive = true } } }
    )
}

@Composable
private fun KycStatusBody(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    tint: Color,
    title: String,
    message: String,
    loading: Boolean,
    primaryLabel: String,
    onPrimary: () -> Unit,
    secondaryLabel: String,
    onSecondary: () -> Unit
) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            Modifier.fillMaxWidth().padding(32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(18.dp)
        ) {
            Surface(shape = RoundedCornerShape(50), color = tint.copy(alpha = 0.12f), modifier = Modifier.size(96.dp)) {
                Box(contentAlignment = Alignment.Center) { Icon(icon, null, tint = tint, modifier = Modifier.size(48.dp)) }
            }
            Text(title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            Text(message, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Center,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            Button(onClick = onPrimary, enabled = !loading,
                modifier = Modifier.fillMaxWidth().height(50.dp), shape = RoundedCornerShape(12.dp)) {
                if (loading) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary)
                else Text(primaryLabel, fontWeight = FontWeight.Bold, fontSize = 15.sp)
            }
            TextButton(onClick = onSecondary) { Text(secondaryLabel) }
        }
    }
}
