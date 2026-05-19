package com.vkenterprises.vras.ui.screens

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
import com.vkenterprises.vras.R
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthViewModel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first

@Composable
fun SplashScreen(vm: AuthViewModel, navigate: (String) -> Unit) {
    LaunchedEffect(Unit) {
        delay(1200)
        // Decide the route from the LOCAL stored session only. The session
        // refresh is a network call — fire it in the background and do NOT
        // wait on it, otherwise a slow connection freezes the splash screen.
        val loggedIn = runCatching { vm.isLoggedIn.first() }.getOrDefault(false)
        if (loggedIn) vm.refreshSession()   // background, not joined
        navigate(if (loggedIn) Screen.Home.route else Screen.Login.route)
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
                contentDescription = "CRMS",
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
