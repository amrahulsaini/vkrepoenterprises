package com.vkenterprises.crmrs.ui.screens

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

internal val RBI_GUIDELINES = listOf(
    "ग्राहक के साथ हमेशा सम्मानपूर्वक और पेशेवर तरीके से व्यवहार करें।",
    "किसी भी प्रकार की धमकी, दबाव या उत्पीड़न न करें।",
    "ग्राहक की व्यक्तिगत एवं ऋण संबंधी जानकारी पूरी तरह गोपनीय रखें।",
    "वसूली से संबंधित कॉल/संपर्क केवल सुबह 8:00 बजे से शाम 7:00 बजे तक करें।",
    "ग्राहक को किसी भी प्रकार की गलत या भ्रामक जानकारी न दें।",
    "विज़िट/वाहन कब्जे (Repossession) के समय वैध पहचान पत्र (ID Card) एवं प्राधिकरण पत्र (Authorization Letter) साथ रखें।",
    "सभी वसूली एवं वाहन कब्जे की कार्रवाई संबंधित बैंक/वित्तीय संस्था की नीति तथा लागू RBI दिशानिर्देशों के अनुसार करें।",
)

@Composable
fun RbiGuidelinesCard() {
    Card(
        shape  = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = Color(0xFFFFF8E1)),
        elevation = CardDefaults.cardElevation(0.dp),
        border = BorderStroke(1.dp, Color(0xFFF57F17).copy(alpha = 0.35f)),
        modifier  = Modifier.fillMaxWidth()
    ) {
        Column(Modifier.padding(16.dp)) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Icon(Icons.Default.Gavel, null,
                    tint = Color(0xFFF57F17), modifier = Modifier.size(18.dp))
                Text(
                    "RBI वसूली दिशानिर्देश",
                    style = MaterialTheme.typography.titleSmall,
                    fontFamily = FontFamily.Default,
                    fontWeight = FontWeight.Bold,
                    color = Color(0xFFF57F17)
                )
            }
            Spacer(Modifier.height(10.dp))
            RBI_GUIDELINES.forEach { line ->
                Row(
                    Modifier.padding(bottom = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Text("•",
                        fontFamily = FontFamily.Default,
                        style = MaterialTheme.typography.bodyMedium,
                        color = Color(0xFFF57F17))
                    Text(
                        line,
                        fontFamily = FontFamily.Default,
                        style = MaterialTheme.typography.bodySmall,
                        lineHeight = 19.sp,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                }
            }
        }
    }
}
