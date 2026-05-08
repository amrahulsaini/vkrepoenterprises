package com.vkenterprises.vras.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.HourglassTop
import androidx.compose.material.icons.filled.PhonelinkLock
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.*

@Composable
fun WaitingApprovalScreen(reason: String, onBackToLogin: () -> Unit) {
    val isDeviceMismatch = reason == "device_mismatch"

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            Modifier
                .fillMaxWidth()
                .padding(40.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            Surface(
                shape = RoundedCornerShape(32.dp),
                color = if (isDeviceMismatch)
                    MaterialTheme.colorScheme.errorContainer
                else
                    MaterialTheme.colorScheme.secondaryContainer,
                modifier = Modifier.size(96.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        imageVector = if (isDeviceMismatch)
                            Icons.Default.PhonelinkLock
                        else
                            Icons.Default.HourglassTop,
                        contentDescription = null,
                        modifier = Modifier.size(48.dp),
                        tint = if (isDeviceMismatch)
                            MaterialTheme.colorScheme.onErrorContainer
                        else
                            MaterialTheme.colorScheme.onSecondaryContainer
                    )
                }
            }

            Text(
                text = if (isDeviceMismatch) "Device Not Verified" else "Awaiting Approval",
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center
            )

            Text(
                text = if (isDeviceMismatch)
                    "Your account is registered on a different device.\nPlease ask your admin to verify this device."
                else
                    "Your account is pending approval from the administrator.\nYou'll be notified once approved.",
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
                lineHeight = 24.sp
            )

            Spacer(Modifier.height(8.dp))

            Card(
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant
                ),
                shape = RoundedCornerShape(12.dp)
            ) {
                Column(
                    Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Text(
                        if (isDeviceMismatch) "What to do?" else "What happens next?",
                        style = MaterialTheme.typography.labelLarge,
                        fontWeight = FontWeight.SemiBold
                    )
                    Text(
                        if (isDeviceMismatch)
                            "• Contact your admin\n• Ask them to reset your device in the admin panel\n• Then log in again from this device"
                        else
                            "• Admin reviews your registration\n• Once approved, you can log in\n• You'll get access to search features",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        lineHeight = 20.sp
                    )
                }
            }

            Spacer(Modifier.height(8.dp))

            Button(
                onClick = onBackToLogin,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(12.dp)
            ) {
                Text("BACK TO LOGIN", fontWeight = FontWeight.Bold)
            }
        }
    }
}
