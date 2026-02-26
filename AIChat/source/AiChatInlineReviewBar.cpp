// -----------------------------------------------------------------------------
// File: AiChatInlineReviewBar.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatInlineReviewBar.hpp"

#include <QEvent>
#include <QFileInfo>
#include <QHBoxLayout>
#include <QLabel>
#include <QPainter>
#include <QPushButton>

namespace AiChat
{

InlineReviewBar::InlineReviewBar(QWidget* aEditorViewport)
   : QWidget(aEditorViewport)
{
   setAutoFillBackground(false);
   setAttribute(Qt::WA_StyledBackground, false);

   auto* layout = new QHBoxLayout(this);
   layout->setContentsMargins(10, 5, 10, 5);
   layout->setSpacing(8);

   mInfoLabel = new QLabel(this);
   mInfoLabel->setStyleSheet("QLabel { color: #c9d1d9; font-size: 12px; background: transparent; }");
   layout->addWidget(mInfoLabel);

   layout->addStretch();

   mAcceptButton = new QPushButton(QStringLiteral("Accept"), this);
   mAcceptButton->setStyleSheet(
      "QPushButton { background-color: #238636; color: white; border: none; "
      "border-radius: 4px; padding: 4px 14px; font-weight: bold; font-size: 11px; }"
      "QPushButton:hover { background-color: #2ea043; }");
   mAcceptButton->setCursor(Qt::PointingHandCursor);
   layout->addWidget(mAcceptButton);

   mRejectButton = new QPushButton(QStringLiteral("Reject"), this);
   mRejectButton->setStyleSheet(
      "QPushButton { background-color: #da3633; color: white; border: none; "
      "border-radius: 4px; padding: 4px 14px; font-weight: bold; font-size: 11px; }"
      "QPushButton:hover { background-color: #f85149; }");
   mRejectButton->setCursor(Qt::PointingHandCursor);
   layout->addWidget(mRejectButton);

   connect(mAcceptButton, &QPushButton::clicked, this, &InlineReviewBar::Accepted);
   connect(mRejectButton, &QPushButton::clicked, this, &InlineReviewBar::Rejected);

   setFixedHeight(32);

   // Install event filter on parent to reposition on resize
   if (aEditorViewport)
   {
      aEditorViewport->installEventFilter(this);
   }
}

void InlineReviewBar::SetInfo(const QString& aFileName, int aAddedLines, int aRemovedLines)
{
   const QString name = QFileInfo(aFileName).fileName();
   QString text = name;
   if (aAddedLines > 0 || aRemovedLines > 0)
   {
      text += QStringLiteral("  ");
      if (aAddedLines > 0)
      {
         text += QStringLiteral("<span style='color:#3fb950;'>+%1</span>").arg(aAddedLines);
      }
      if (aRemovedLines > 0)
      {
         if (aAddedLines > 0) text += QStringLiteral(" ");
         text += QStringLiteral("<span style='color:#f85149;'>-%1</span>").arg(aRemovedLines);
      }
   }
   mInfoLabel->setTextFormat(Qt::RichText);
   mInfoLabel->setText(text);
   adjustSize();
   Reposition();
}

void InlineReviewBar::Reposition()
{
   if (!parentWidget()) return;

   const int parentWidth = parentWidget()->width();
   const int barWidth = qMin(sizeHint().width() + 20, parentWidth - 20);
   setFixedWidth(barWidth);

   const int x = parentWidth - barWidth - 8;
   const int y = 8;
   move(x, y);
   raise();
}

bool InlineReviewBar::eventFilter(QObject* aObj, QEvent* aEvent)
{
   if (aObj == parentWidget() && aEvent->type() == QEvent::Resize)
   {
      Reposition();
   }
   return QWidget::eventFilter(aObj, aEvent);
}

void InlineReviewBar::paintEvent(QPaintEvent* /*aEvent*/)
{
   QPainter painter(this);
   painter.setRenderHint(QPainter::Antialiasing);

   // Dark semi-transparent background
   painter.setBrush(QColor(30, 33, 39, 235));
   painter.setPen(QPen(QColor(48, 54, 61), 1));
   painter.drawRoundedRect(rect().adjusted(0, 0, -1, -1), 8, 8);
}

} // namespace AiChat
