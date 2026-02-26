// -----------------------------------------------------------------------------
// File: AiChatInlineReviewBar.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_INLINE_REVIEW_BAR_HPP
#define AICHAT_INLINE_REVIEW_BAR_HPP

#include <QWidget>

class QLabel;
class QPushButton;

namespace AiChat
{

/// Floating toolbar that appears at the top of an editor when AI changes
/// are pending review.  Provides Accept (keep) / Reject (undo) controls.
/// Mirrors the GitHub Copilot Edits inline review experience.
class InlineReviewBar : public QWidget
{
   Q_OBJECT
public:
   explicit InlineReviewBar(QWidget* aEditorViewport);

   /// Set the summary text shown on the bar.
   void SetInfo(const QString& aFileName, int aAddedLines, int aRemovedLines);

   /// Reposition the bar at the top-right of the parent widget.
   void Reposition();

signals:
   void Accepted();
   void Rejected();

protected:
   bool eventFilter(QObject* aObj, QEvent* aEvent) override;
   void paintEvent(QPaintEvent* aEvent) override;

private:
   QLabel*      mInfoLabel;
   QPushButton* mAcceptButton;
   QPushButton* mRejectButton;
};

} // namespace AiChat

#endif // AICHAT_INLINE_REVIEW_BAR_HPP
