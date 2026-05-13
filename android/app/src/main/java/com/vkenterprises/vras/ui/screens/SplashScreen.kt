package com.vkenterprises.vras.ui.screens

import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthViewModel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first

@Composable
fun SplashScreen(vm: AuthViewModel, navigate: (String) -> Unit) {
    val primary = MaterialTheme.colorScheme.primary

    LaunchedEffect(Unit) {
        delay(1200)
        val loggedIn = vm.isLoggedIn.first()
        if (loggedIn) vm.refreshSession().join()
        navigate(if (loggedIn) Screen.Home.route else Screen.Login.route)
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(primary),
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text("VK", color = MaterialTheme.colorScheme.onPrimary,
                fontSize = 72.sp, fontWeight = FontWeight.Black)
            Text("ENTERPRISES",
                color = MaterialTheme.colorScheme.onPrimary.copy(alpha = .85f),
                fontSize = 18.sp, fontWeight = FontWeight.SemiBold,
                letterSpacing = 6.sp)
            Spacer(Modifier.height(32.dp))
            CircularProgressIndicator(
                color = MaterialTheme.colorScheme.onPrimary.copy(alpha = .7f),
                strokeWidth = 2.dp, modifier = Modifier.size(28.dp))
        }
    }
}
