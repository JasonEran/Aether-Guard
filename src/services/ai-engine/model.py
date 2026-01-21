from __future__ import annotations

from pathlib import Path
from typing import Sequence

import torch
from torch import nn


class LSTMPredictor(nn.Module):
    def __init__(self, input_size: int = 2, hidden_size: int = 64, num_layers: int = 2) -> None:
        super().__init__()
        self.lstm = nn.LSTM(
            input_size=input_size,
            hidden_size=hidden_size,
            num_layers=num_layers,
            batch_first=True,
        )
        self.fc = nn.Linear(hidden_size, 1)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        output, _ = self.lstm(x)
        last_step = output[:, -1, :]
        return self.fc(last_step)

    def load(self, path: Path | str) -> "LSTMPredictor":
        state = torch.load(path, map_location="cpu")
        self.load_state_dict(state)
        self.eval()
        return self

    def predict(self, sequence: Sequence[Sequence[float]]) -> float:
        self.eval()
        with torch.no_grad():
            tensor = torch.tensor(sequence, dtype=torch.float32)
            if tensor.dim() == 2:
                tensor = tensor.unsqueeze(0)
            output = self.forward(tensor)
            return float(output.squeeze().item())
