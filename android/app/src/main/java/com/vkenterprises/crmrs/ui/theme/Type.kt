package com.vkenterprises.crmrs.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import com.vkenterprises.crmrs.R

val RobotoFamily = FontFamily(
    Font(R.font.roboto_regular, FontWeight.Normal),
    Font(R.font.roboto_medium,  FontWeight.Medium),
    Font(R.font.roboto_bold,    FontWeight.Bold),
    Font(R.font.roboto_black,   FontWeight.Black)
)

private val base = Typography()

val VKTypography = Typography(
    displayLarge   = base.displayLarge.copy(fontFamily = RobotoFamily),
    displayMedium  = base.displayMedium.copy(fontFamily = RobotoFamily),
    displaySmall   = base.displaySmall.copy(fontFamily = RobotoFamily),
    headlineLarge  = base.headlineLarge.copy(fontFamily = RobotoFamily),
    headlineMedium = base.headlineMedium.copy(fontFamily = RobotoFamily),
    headlineSmall  = base.headlineSmall.copy(fontFamily = RobotoFamily),
    titleLarge     = base.titleLarge.copy(fontFamily = RobotoFamily),
    titleMedium    = base.titleMedium.copy(fontFamily = RobotoFamily),
    titleSmall     = base.titleSmall.copy(fontFamily = RobotoFamily),
    bodyLarge      = base.bodyLarge.copy(fontFamily = RobotoFamily),
    bodyMedium     = base.bodyMedium.copy(fontFamily = RobotoFamily),
    bodySmall      = base.bodySmall.copy(fontFamily = RobotoFamily),
    labelLarge     = base.labelLarge.copy(fontFamily = RobotoFamily),
    labelMedium    = base.labelMedium.copy(fontFamily = RobotoFamily),
    labelSmall     = base.labelSmall.copy(fontFamily = RobotoFamily)
)
