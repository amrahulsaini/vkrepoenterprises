package com.vkenterprises.crmrs.ui.theme

import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val LightColors = lightColorScheme(
    primary          = Orange40,
    onPrimary        = Color.White,
    primaryContainer = Orange90,
    onPrimaryContainer = Orange10,
    secondary        = Orange30,
    onSecondary      = Color.White,
    secondaryContainer = Orange90,
    onSecondaryContainer = Orange10,
    background       = Color.White,
    surface          = Color.White,
    surfaceVariant   = Color(0xFFF2F2F2),
    onSurface        = OrangeGray10,
    onSurfaceVariant = OrangeGray20,
    error            = Red40,
    onError          = Color.White,
)

private val DarkColors = darkColorScheme(
    primary          = Orange80,
    onPrimary        = Orange20,
    primaryContainer = Orange30,
    onPrimaryContainer = Orange90,
    background       = OrangeGray10,
    surface          = OrangeGray10,
)

@Composable
fun VKTheme(
    darkTheme: Boolean = false,
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography  = Typography(),
        content     = content
    )
}
