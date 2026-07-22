package com.vkenterprises.crmrs.ui.screens

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import com.vkenterprises.crmrs.R
import com.vkenterprises.crmrs.navigation.Screen
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import kotlinx.coroutines.flow.first

@Composable
fun SplashScreen(vm: AuthViewModel, navigate: (String) -> Unit) {
    LaunchedEffect(Unit) {
        // Require BOTH the logged-in flag AND a real session token. If logout
        // was interrupted and only half-cleared, or a token was wiped, treat it
        // as logged out rather than auto-entering Home with no valid session.
        val loggedIn = runCatching { vm.isLoggedIn.first() }.getOrDefault(false)
        val hasToken = runCatching { !vm.tenantToken.first().isNullOrBlank() }.getOrDefault(false)
        val enter = loggedIn && hasToken
        if (enter) vm.refreshSession()
        navigate(if (enter) Screen.Home.route else Screen.Login.route)
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(Color.White),
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Image(
                painter = painterResource(id = R.drawable.crmrs_logo),
                contentDescription = "CRMRS",
                contentScale = ContentScale.Fit,
                modifier = Modifier.size(200.dp)
            )
            Spacer(Modifier.height(28.dp))
            CircularProgressIndicator(
                color = MaterialTheme.colorScheme.primary,
                strokeWidth = 2.dp,
                modifier = Modifier.size(28.dp)
            )
        }
    }
}
